using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using Pcs7Mcp;
using Pcs7Mcp.Backend;
using Xunit;

namespace Pcs7Tests;

/// <summary>Command-line options and the HTTP client towards Pcs7Agent (against a fake agent on localhost).</summary>
public sealed class RemoteTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "pcs7-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Local_mode_by_default_read_only()
    {
        var o = ServerOptions.Parse(Array.Empty<string>());
        Assert.False(o.IsRemote);
        Assert.Equal(Pcs7Core.AccessMode.ReadOnly, o.AccessMode);
    }

    [Theory]
    [InlineData("192.168.56.10", "http://192.168.56.10:8765")]
    [InlineData("http://vm", "http://vm:8765")]
    [InlineData("http://vm:9000/", "http://vm:9000")]
    [InlineData("http://vm:80", "http://vm:80")]
    public void Agent_url_gets_the_default_port(string given, string expected)
        => Assert.Equal(expected, ServerOptions.Parse(new[] { "--agent", given, "--agent-token", "t" }).AgentUrl);

    [Fact]
    public void Agent_requires_a_token_and_plain_http()
    {
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(new[] { "--agent", "vm" }));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(new[] { "--agent", "https://vm:8765", "--agent-token", "t" }));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(new[] { "--access-mode", "admin" }));
    }

    [Fact]
    public async Task Exports_are_copied_locally_but_never_outside_the_copies_folder()
    {
        const string remoteOk = @"C:\ProgramData\Pcs7Agent\export\P\symbols\S7.sdf";
        const string remoteEvil = @"C:\ProgramData\Pcs7Agent\export\evil.txt";
        FakeAgent agent = null!;
        agent = new FakeAgent(_ => new JObject
        {
            ["success"] = true,
            ["result"] = new JObject { ["file"] = remoteOk, ["evil"] = remoteEvil, ["ts"] = "2026-09-26T20:12:56+02:00" },
            ["files"] = new JArray(
                new JObject { ["relative"] = @"P\symbols\S7.sdf", ["path"] = remoteOk },
                // sibling folder sharing the prefix of the local copies folder (remote\127.0.0.1_<port>)
                new JObject { ["relative"] = $@"..\127.0.0.1_{new Uri(agent.Url).Port}0\evil.txt", ["path"] = remoteEvil }),
        });
        using var _ = agent;
        using var backend = Backend(agent);

        var result = await backend.InvokeAsync("s7_export_symbols", new JObject { ["project"] = "P", ["program"] = "S7" });

        var local = (string)result["file"]!;
        Assert.StartsWith(_work, local);
        Assert.Equal("content", File.ReadAllText(local));
        Assert.Equal(remoteEvil, (string)result["evil"]!); // not downloaded, path left untouched
        Assert.Single(Directory.GetFiles(_work, "*", SearchOption.AllDirectories));
        Assert.Equal("2026-09-26T20:12:56+02:00", (string)result["ts"]!);
    }

    [Fact]
    public async Task Values_reach_the_agent_unchanged_and_local_files_are_uploaded()
    {
        var file = Path.Combine(Path.GetTempPath(), "pcs7-tests-FC1.scl");
        File.WriteAllText(file, "FUNCTION FC1");
        JObject? received = null;
        using var agent = new FakeAgent(body => { received = body; return new JObject { ["success"] = true, ["result"] = "ok" }; });
        using var backend = Backend(agent);
        try
        {
            await backend.InvokeAsync("opc_write", new JObject { ["nodeId"] = "n", ["value"] = "2024-05-01T10:00:00" });
            Assert.Equal("2024-05-01T10:00:00", (string)received!["value"]!);

            await backend.InvokeAsync("s7_import_source", new JObject { ["filePath"] = file });
            Assert.Equal("pcs7-tests-FC1.scl", (string)received!["filePath"]!); // only the name, not the PC path
            Assert.Equal("FUNCTION FC1", Encoding.UTF8.GetString(Convert.FromBase64String((string)received["filePath__content"]!)));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task Agent_errors_become_readable_exceptions()
    {
        using (var agent = new FakeAgent(_ => new JObject { ["success"] = false, ["error"] = "read-only station" }))
        using (var backend = Backend(agent))
            Assert.Equal("read-only station", (await Assert.ThrowsAsync<InvalidOperationException>(() => backend.InvokeAsync("s7_save", new JObject()))).Message);

        using (var agent = new FakeAgent(_ => null)) // null = 401
        using (var backend = Backend(agent))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => backend.StatusAsync());

        using (var backend = new RemoteBackend(Options("http://127.0.0.1:1")))
            Assert.Contains("not reachable", (await Assert.ThrowsAsync<InvalidOperationException>(() => backend.StatusAsync())).Message);
    }

    private RemoteBackend Backend(FakeAgent agent) => new(Options(agent.Url));

    private ServerOptions Options(string url) =>
        ServerOptions.Parse(new[] { "--agent", url, "--agent-token", "t", "--workdir", _work, "--agent-timeout", "1" });

    public void Dispose()
    {
        try { Directory.Delete(_work, true); } catch { }
    }

    /// <summary>Minimal Pcs7Agent: answers every POST/GET health with the given envelope, every file request with "content".</summary>
    private sealed class FakeAgent : IDisposable
    {
        private readonly HttpListener _listener = new();

        public FakeAgent(Func<JObject, JObject?> answer)
        {
            var port = 18900 + Random.Shared.Next(0, 900);
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); } catch { return; }
                    byte[] bytes;
                    if (ctx.Request.Url!.AbsolutePath == "/api/file")
                        bytes = "content"u8.ToArray();
                    else
                    {
                        // parsed like the real agent does (no date conversion), so the test sees what the client sent
                        var body = ctx.Request.HasEntityBody ? (JObject)Pcs7Core.JsonUtil.Parse(new StreamReader(ctx.Request.InputStream).ReadToEnd()) : new JObject();
                        var envelope = answer(body);
                        if (envelope is null) ctx.Response.StatusCode = 401;
                        bytes = Encoding.UTF8.GetBytes((envelope ?? new JObject { ["success"] = false }).ToString());
                    }
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            });
        }

        public string Url { get; }

        public void Dispose() { try { _listener.Stop(); } catch { } }
    }
}
