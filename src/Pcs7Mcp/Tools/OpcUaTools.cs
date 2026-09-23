using System.ComponentModel;
using ModelContextProtocol.Server;
using Pcs7Mcp.OpcUa;

namespace Pcs7Mcp.Tools;

[McpServerToolType]
public sealed class OpcUaReadTools(OpcUaSession opc)
{
    private const string EndpointHelp = "OPC UA endpoint (default opc.tcp://localhost:4863, OpenPCS 7 UA server)";

    [McpServerTool(Name = "opc_status", ReadOnly = true, Idempotent = true),
     Description("Connects to the OpenPCS 7 OPC UA server (process runtime) and returns server state, product and namespaces. Requires the PCS 7 OS runtime to be active.")]
    public Task<string> Status([Description(EndpointHelp)] string? endpoint = null)
        => ToolRunner.RunAsync(() => opc.StatusAsync(endpoint));

    [McpServerTool(Name = "opc_browse", ReadOnly = true, Idempotent = true),
     Description("Browses the child nodes (objects, variables) of an OPC UA node. Without nodeId starts from the Objects folder.")]
    public Task<string> Browse(
        [Description("NodeId to browse, e.g. ns=2;s=... (default Objects folder)")] string? nodeId = null,
        [Description("Max children to return (default 200)")] int maxResults = 200,
        [Description(EndpointHelp)] string? endpoint = null)
        => ToolRunner.RunAsync(() => opc.BrowseAsync(endpoint, nodeId, maxResults));

    [McpServerTool(Name = "opc_read", ReadOnly = true, Idempotent = true),
     Description("Reads current values (value, type, quality, timestamp) of one or more OPC UA variables, e.g. PCS 7 tags.")]
    public Task<string> Read(
        [Description("NodeIds to read")] string[] nodeIds,
        [Description(EndpointHelp)] string? endpoint = null)
        => ToolRunner.RunAsync(() => opc.ReadAsync(endpoint, nodeIds));
}

/// <summary>Registered only in read-write mode.</summary>
[McpServerToolType]
public sealed class OpcUaWriteTools(OpcUaSession opc)
{
    [McpServerTool(Name = "opc_write", Destructive = true),
     Description("Writes a value to an OPC UA variable of the running process (e.g. a setpoint). ACTS ON THE LIVE PLANT. Previews first; executes only with confirm=true.")]
    public async Task<string> Write(
        [Description("NodeId of the variable")] string nodeId,
        [Description("Value to write (converted to the variable's data type)")] string value,
        [Description("false (default) = preview with current value; true = write (only after explicit user approval)")] bool confirm = false,
        [Description("OPC UA endpoint (default opc.tcp://localhost:4863)")] string? endpoint = null)
    {
        if (!confirm)
        {
            var current = await ToolRunner.RunAsync(() => opc.ReadAsync(endpoint, new[] { nodeId }));
            return ToolRunner.Preview("Write value to live process variable", new { nodeId, newValue = value, currentValue = current });
        }
        return await ToolRunner.RunAsync(() => opc.WriteAsync(endpoint, nodeId, value));
    }
}
