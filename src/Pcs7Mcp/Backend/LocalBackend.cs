using Newtonsoft.Json.Linq;
using Pcs7Core;
using Pcs7Core.Operations;

namespace Pcs7Mcp.Backend;

/// <summary>PCS 7 installed on this machine: operations run in-process.</summary>
public sealed class LocalBackend(ServerOptions options) : IPcs7Backend, IDisposable
{
    private readonly Pcs7Operations _ops = new(options.ToCoreOptions());

    public Task<JToken> InvokeAsync(string operation, JObject args) => _ops.InvokeAsync(operation, args);

    public Task<JToken> StatusAsync()
    {
        var info = JObject.FromObject(StationInfo.Collect(_ops.Options, "Pcs7McpServer (local mode)"), Pcs7Operations.Serializer);
        return Task.FromResult<JToken>(new JObject { ["mode"] = "local", ["station"] = info });
    }

    public void Dispose() => _ops.Dispose();
}
