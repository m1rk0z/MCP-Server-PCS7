using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;
using Pcs7Core;
using Pcs7Core.Operations;

namespace Pcs7Mcp.Backend;

/// <summary>
/// PCS 7 installed on another machine (typically a VM): every operation is sent to Pcs7Agent over HTTP.
/// Files to import are uploaded with the request; files produced on the PCS 7 machine (exports, logs)
/// are downloaded into the local work folder and their paths in the result are rewritten to the local copy.
/// </summary>
public sealed class RemoteBackend : IPcs7Backend, IDisposable
{
    private const long MaxUploadBytes = 20L * 1024 * 1024; // must fit the agent request limit (32 MB) once base64-encoded

    private readonly ServerOptions _options;
    private readonly HttpClient _http;
    private readonly string _localDir;

    public RemoteBackend(ServerOptions options)
    {
        _options = options;
        _http = new HttpClient { BaseAddress = new Uri(options.AgentUrl! + "/"), Timeout = options.AgentTimeout };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.AgentToken);
        _http.DefaultRequestHeaders.Add(AgentProtocol.VersionHeader, AgentProtocol.Version.ToString());
        var uri = new Uri(options.AgentUrl!);
        _localDir = Path.Combine(options.WorkDir, "remote", $"{uri.Host}_{uri.Port}");
    }

    public async Task<JToken> InvokeAsync(string operation, JObject args)
    {
        var body = (JObject)args.DeepClone();
        foreach (var name in Pcs7Operations.FileArguments) AttachFile(body, name);

        var response = await Send(() => _http.PostAsync(AgentProtocol.InvokePath.TrimStart('/') + Uri.EscapeDataString(operation),
            new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")));
        var envelope = await ReadEnvelope(response);

        var result = envelope["result"] ?? JValue.CreateNull();
        if (envelope["files"] is JArray files)
        {
            foreach (var f in files.OfType<JObject>())
            {
                var rel = (string?)f["relative"];
                var remote = (string?)f["path"];
                if (rel is null || remote is null) continue;
                var local = await Download(rel);
                if (local is not null) ReplaceStrings(result, remote, local);
            }
        }
        return result;
    }

    public async Task<JToken> StatusAsync()
    {
        var response = await Send(() => _http.GetAsync(AgentProtocol.HealthPath.TrimStart('/')));
        var envelope = await ReadEnvelope(response);
        return new JObject
        {
            ["mode"] = "remote",
            ["agentUrl"] = _options.AgentUrl,
            ["localCopies"] = _localDir,
            ["station"] = envelope["result"],
        };
    }

    /// <summary>
    /// A path that exists on this PC is uploaded with the request (the agent stores it in its work folder).
    /// A path that does not exist here is passed unchanged: it is taken as a path on the PCS 7 machine.
    /// </summary>
    private static void AttachFile(JObject body, string name)
    {
        var path = (string?)body[name];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var info = new FileInfo(path);
        if (info.Length > MaxUploadBytes) throw new InvalidOperationException($"File too large to send to the PCS 7 machine ({info.Length / 1024 / 1024} MB): {path}");
        body[name + Pcs7Operations.UploadContentSuffix] = Convert.ToBase64String(File.ReadAllBytes(path));
        body[name] = info.Name;
    }

    private async Task<string?> Download(string relative)
    {
        try
        {
            var root = Path.GetFullPath(_localDir).TrimEnd('\\') + "\\";
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null; // never outside the local copies folder
            using var response = await _http.GetAsync(AgentProtocol.FilePath.TrimStart('/') + "?path=" + Uri.EscapeDataString(relative));
            if (!response.IsSuccessStatusCode) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using (var fs = File.Create(target))
                await response.Content.CopyToAsync(fs);
            return target;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[pcs7-mcp] cannot download {relative} from agent: {ex.Message}");
            return null;
        }
    }

    private static void ReplaceStrings(JToken token, string from, string to)
    {
        IEnumerable<JToken> all = token is JContainer c ? c.DescendantsAndSelf() : new[] { token };
        foreach (var v in all.OfType<JValue>().ToList())
            if (v.Type == JTokenType.String && string.Equals((string?)v.Value, from, StringComparison.OrdinalIgnoreCase))
                v.Value = to;
    }

    private async Task<HttpResponseMessage> Send(Func<Task<HttpResponseMessage>> request)
    {
        try { return await request(); }
        catch (TaskCanceledException)
        {
            throw new TimeoutException($"The PCS 7 agent at {_options.AgentUrl} did not answer within {_options.AgentTimeout.TotalMinutes:0} minutes.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"PCS 7 agent not reachable at {_options.AgentUrl} ({ex.Message}). " +
                "Check that the VM is running, Pcs7Agent is started (tray icon) and the port is open in the VM firewall.");
        }
    }

    private async Task<JObject> ReadEnvelope(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new UnauthorizedAccessException("The PCS 7 agent rejected the token: check PCS7_MCP_AGENT_TOKEN against the token shown by the agent on the VM.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new UnauthorizedAccessException("The PCS 7 agent does not accept requests from this PC (allowed clients list).");
            JObject envelope;
            try { envelope = (JObject)JsonUtil.Parse(text); }
            catch { throw new InvalidOperationException($"Unexpected answer from the PCS 7 agent (HTTP {(int)response.StatusCode}): {Truncate(text, 300)}"); }
            if (envelope.Value<bool?>("success") != true)
                throw new InvalidOperationException((string?)envelope["error"] ?? $"PCS 7 agent error (HTTP {(int)response.StatusCode})");
            return envelope;
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    public void Dispose() => _http.Dispose();
}
