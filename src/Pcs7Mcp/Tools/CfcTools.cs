using System.ComponentModel;
using ModelContextProtocol.Server;
using Pcs7Mcp.Backend;
using static Pcs7Mcp.Tools.ToolRunner;

namespace Pcs7Mcp.Tools;

/// <summary>
/// Read-only access to CFC chart contents (blocks, pin values, interconnections, tag connections).
/// The work is done by Pcs7CfcReader.exe on the PCS 7 machine; the project is never modified.
/// </summary>
[McpServerToolType]
public sealed class CfcTools(IPcs7Backend pcs7)
{
    [McpServerTool(Name = "cfc_list_charts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Lists the CFC charts of a PCS 7 project directly from the CFC database: chart name, comment, number of blocks and block types used. The CFC editor should be closed for this project.")]
    public Task<string> ListCharts(
        [Description("Project name or path (as returned by s7_list_projects)")] string project,
        [Description("Optional text filter on chart name")] string? filter = null)
        => RunAsync(() => pcs7.InvokeAsync("cfc_list_charts", Args(new { project, filter })));

    [McpServerTool(Name = "cfc_read_charts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Reads the full content of one or more CFC charts: block instances (name, type, FB/FC, comment) and their pins with direction, data type, value, 'changed' flag (value differs from the block type default), block interconnections (from/to 'chart\\block.pin'), and connections to shared addresses (tag symbol/address). By default only changed or connected pins are returned.")]
    public Task<string> ReadCharts(
        [Description("Project name or path")] string project,
        [Description("Chart names, e.g. [\"7101-PT02\"]")] string[] charts,
        [Description("Return only pins with a changed value or a connection (default true). false = all pins incl. comments.")] bool changedOnly = true)
        => RunAsync(() => pcs7.InvokeAsync("cfc_read_charts", Args(new { project, charts, changedOnly })));

    [McpServerTool(Name = "cfc_export_charts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Exports the content of all (or filtered) CFC charts of a project to a JSON file in the export folder and returns only a summary (path, chart and block counts). Use it for large projects; read the file afterwards.")]
    public Task<string> ExportCharts(
        [Description("Project name or path")] string project,
        [Description("Optional text filter on chart name")] string? filter = null,
        [Description("Only pins with a changed value or a connection (default true)")] bool changedOnly = true)
        => RunAsync(() => pcs7.InvokeAsync("cfc_export_charts", Args(new { project, filter, changedOnly })));
}
