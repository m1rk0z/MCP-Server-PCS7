using System.ComponentModel;
using ModelContextProtocol.Server;
using Pcs7Mcp.Backend;
using static Pcs7Mcp.Tools.ToolRunner;

namespace Pcs7Mcp.Tools;

[McpServerToolType]
public sealed class StatusTools(IPcs7Backend pcs7)
{
    [McpServerTool(Name = "pcs7_status", ReadOnly = true, Idempotent = true),
     Description("Shows where PCS 7 operations run (local, or remote agent on a VM) and the state of the PCS 7 machine: Windows version, access mode, SIMATIC command interface available, CFC reader, work folder. Use it first to check the connection.")]
    public Task<string> Status() => RunAsync(() => pcs7.StatusAsync());
}
