using Newtonsoft.Json.Linq;

namespace Pcs7Mcp.Backend;

/// <summary>Where PCS 7 operations run: in this process (local mode) or on the PCS 7 machine through Pcs7Agent (remote mode).</summary>
public interface IPcs7Backend
{
    /// <summary>Runs an operation (named like the MCP tool) and returns its result; throws on failure.</summary>
    Task<JToken> InvokeAsync(string operation, JObject args);

    /// <summary>Information about the PCS 7 side (machine, OS, access mode, SIMATIC availability).</summary>
    Task<JToken> StatusAsync();
}
