using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pcs7Core;
using Pcs7Core.Com;
using Pcs7Core.Operations;

namespace Pcs7Agent;

/// <summary>HTTP endpoint of the agent (see <see cref="AgentProtocol"/>). One request = one PCS 7 operation.</summary>
public sealed class AgentServer : IDisposable
{
    // 20 MB upload as base64 (+33%) plus JSON; kept low because the agent is a 32-bit process.
    private const long MaxBodyBytes = 32L * 1024 * 1024;

    private readonly AgentConfig _config;
    private readonly Pcs7Operations _ops;
    private readonly HttpListener _listener = new();
    private readonly byte[] _token;
    private volatile bool _running;

    public AgentServer(AgentConfig config)
    {
        _config = config;
        _token = Encoding.UTF8.GetBytes(config.Token);
        _ops = new Pcs7Operations(config.ToCoreOptions());
    }

    public string Prefix { get; private set; } = "";

    /// <summary>Starts listening on all interfaces (needs the URL reservation made by the installer).</summary>
    public void Start()
    {
        Prefix = $"http://{(string.IsNullOrWhiteSpace(_config.ListenHost) ? "+" : _config.ListenHost)}:{_config.Port}/";
        _listener.Prefixes.Add(Prefix);
        try { _listener.Start(); }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            throw new InvalidOperationException(
                $"Access denied on port {_config.Port}: the URL reservation is missing. Run setup.cmd as administrator.", ex);
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 32 || ex.ErrorCode == 183)
        {
            throw new InvalidOperationException($"Port {_config.Port} is already in use by another program.", ex);
        }
        _running = true;
        Task.Run(AcceptLoop);
        AgentLog.Write($"listening on {Prefix} - access mode {_config.AccessMode}, work folder {_config.WorkDir}");
    }

    private async Task AcceptLoop()
    {
        while (_running)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) when (!_running) { return; }
            catch (Exception ex) { AgentLog.Write("accept error: " + ex.Message); continue; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var sw = Stopwatch.StartNew();
        var remote = ctx.Request.RemoteEndPoint?.Address;
        var path = ctx.Request.Url?.AbsolutePath ?? "";
        string outcome = "?";
        try
        {
            if (_config.AllowedClients.Count > 0 && (remote is null || !_config.AllowedClients.Contains(Normalize(remote))))
            {
                outcome = "forbidden";
                await Json(ctx, 403, new JObject { ["success"] = false, ["error"] = "Client not allowed" });
                return;
            }
            if (!Authorized(ctx.Request))
            {
                outcome = "unauthorized";
                await Task.Delay(500); // slows down token guessing
                await Json(ctx, 401, new JObject { ["success"] = false, ["error"] = "Invalid or missing token" });
                return;
            }

            if (path.Equals(AgentProtocol.HealthPath, StringComparison.OrdinalIgnoreCase))
            {
                var info = JObject.FromObject(StationInfo.Collect(_ops.Options, "Pcs7Agent"), Pcs7Operations.Serializer);
                info["protocol"] = AgentProtocol.Version;
                info["port"] = _config.Port;
                info["allowedClients"] = new JArray(_config.AllowedClients);
                outcome = "ok";
                await Json(ctx, 200, new JObject { ["success"] = true, ["result"] = info });
            }
            else if (path.StartsWith(AgentProtocol.InvokePath, StringComparison.OrdinalIgnoreCase) && ctx.Request.HttpMethod == "POST")
            {
                var op = Uri.UnescapeDataString(path.Substring(AgentProtocol.InvokePath.Length));
                path = op;
                var args = await ReadBody(ctx.Request);
                try
                {
                    var result = await _ops.InvokeAsync(op, args);
                    var files = new JArray(_ops.ProducedFiles(result).Select(rel => new JObject
                    {
                        ["relative"] = rel,
                        ["path"] = _ops.ResolveWorkFile(rel),
                    }));
                    outcome = "ok";
                    await Json(ctx, 200, new JObject { ["success"] = true, ["result"] = result, ["files"] = files });
                }
                catch (Exception ex)
                {
                    outcome = "error: " + ComObj.Describe(ex);
                    await Json(ctx, 200, new JObject { ["success"] = false, ["error"] = ComObj.Describe(ex) });
                }
            }
            else if (path.Equals(AgentProtocol.FilePath, StringComparison.OrdinalIgnoreCase) && ctx.Request.HttpMethod == "GET")
            {
                var rel = ctx.Request.QueryString["path"] ?? "";
                path += " " + rel;
                var file = _ops.ResolveWorkFile(rel);
                if (!File.Exists(file))
                {
                    outcome = "not found";
                    await Json(ctx, 404, new JObject { ["success"] = false, ["error"] = "File not found" });
                    return;
                }
                ctx.Response.ContentType = "application/octet-stream";
                using (var fs = File.OpenRead(file))
                {
                    ctx.Response.ContentLength64 = fs.Length;
                    await fs.CopyToAsync(ctx.Response.OutputStream);
                }
                ctx.Response.Close();
                outcome = "ok";
            }
            else
            {
                outcome = "not found";
                await Json(ctx, 404, new JObject { ["success"] = false, ["error"] = "Unknown endpoint" });
            }
        }
        catch (Exception ex)
        {
            outcome = "failed: " + ex.Message;
            try { await Json(ctx, 400, new JObject { ["success"] = false, ["error"] = ex.Message }); } catch { }
        }
        finally
        {
            AgentLog.Write($"{remote} {ctx.Request.HttpMethod} {path} -> {outcome} ({sw.ElapsedMilliseconds} ms)");
        }
    }

    private static string Normalize(IPAddress a) => (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToString();

    private bool Authorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"] ?? "";
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var given = Encoding.UTF8.GetBytes(header.Substring(7).Trim());
        // Constant-time comparison.
        var diff = given.Length ^ _token.Length;
        for (int i = 0; i < Math.Min(given.Length, _token.Length); i++) diff |= given[i] ^ _token[i];
        return diff == 0;
    }

    /// <summary>Parses the JSON body straight from the network stream (no intermediate copies), capped in size.</summary>
    private static Task<JObject> ReadBody(HttpListenerRequest request)
    {
        if (request.ContentLength64 > MaxBodyBytes) throw new InvalidOperationException("Request too large");
        if (request.ContentLength64 == 0) return Task.FromResult(new JObject());
        using var reader = new StreamReader(new LimitedStream(request.InputStream, MaxBodyBytes), Encoding.UTF8);
        return Task.FromResult(JsonUtil.Read(reader) as JObject ?? throw new InvalidOperationException("JSON object expected"));
    }

    /// <summary>Read-only stream wrapper that fails once more than <c>max</c> bytes have been read.</summary>
    private sealed class LimitedStream(Stream inner, long max) : Stream
    {
        private long _read;
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = inner.Read(buffer, offset, count);
            if ((_read += n) > max) throw new InvalidOperationException("Request too large");
            return n;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task Json(HttpListenerContext ctx, int status, JObject body)
    {
        var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _running = false;
        try { _listener.Stop(); _listener.Close(); } catch { }
        _ops.Dispose();
    }
}
