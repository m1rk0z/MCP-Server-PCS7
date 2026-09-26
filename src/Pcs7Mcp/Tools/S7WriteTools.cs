using System.ComponentModel;
using ModelContextProtocol.Server;
using Pcs7Mcp.Backend;
using static Pcs7Mcp.Tools.ToolRunner;

namespace Pcs7Mcp.Tools;

/// <summary>Registered only in read-write mode. Every tool previews first and acts only with confirm=true.</summary>
[McpServerToolType]
public sealed class S7WriteTools(IPcs7Backend pcs7)
{
    private const string FileHelp = "Absolute path of the file on this PC (sent to the PCS 7 machine automatically; a path that does not exist here is used as a path on the PCS 7 machine):";
    private const string ConfirmHelp = "false (default) = preview only; true = execute (only after explicit user approval)";

    [McpServerTool(Name = "s7_import_source", Destructive = true),
     Description("Imports an STL/SCL source file from disk into the Sources folder of a program. Does not compile it.")]
    public Task<string> ImportSource(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program,
        [Description(FileHelp + " .awl/.scl/.inp source file")] string filePath,
        [Description("Source name in the project (default: file name)")] string? sourceName = null,
        [Description("Replace an existing source with the same name")] bool overwrite = false,
        [Description(ConfirmHelp)] bool confirm = false)
    {
        if (!confirm) return Task.FromResult(Preview("Import source into project", new { project, program, filePath, sourceName, overwrite }));
        return RunAsync(() => pcs7.InvokeAsync("s7_import_source", Args(new { project, program, filePath, sourceName, overwrite })));
    }

    [McpServerTool(Name = "s7_compile_source", Destructive = true),
     Description("Compiles an STL/SCL source of the program: creates or overwrites the blocks defined in it. Returns the compiler log.")]
    public Task<string> CompileSource(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program,
        [Description("Source name")] string source,
        [Description(ConfirmHelp)] bool confirm = false)
    {
        if (!confirm) return Task.FromResult(Preview("Compile source (blocks defined in it will be created/overwritten)", new { project, program, source }));
        return RunAsync(() => pcs7.InvokeAsync("s7_compile_source", Args(new { project, program, source })));
    }

    [McpServerTool(Name = "s7_compile_charts", Destructive = true),
     Description("Compiles all CFC/SFC charts of a program (chart folder compile, generates the program blocks). Returns the log. Does not download to the PLC.")]
    public Task<string> CompileCharts(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program,
        [Description(ConfirmHelp)] bool confirm = false)
    {
        if (!confirm) return Task.FromResult(Preview("Compile all CFC/SFC charts of the program", new { project, program }));
        return RunAsync(() => pcs7.InvokeAsync("s7_compile_charts", Args(new { project, program })));
    }

    [McpServerTool(Name = "s7_compile_station", Destructive = true),
     Description("Compiles the hardware configuration of a station (generates system data) or only runs the consistency check.")]
    public Task<string> CompileStation(
        [Description("Project name or path")] string project,
        [Description("Station name")] string station,
        [Description("true = consistency check only, nothing generated")] bool consistencyCheckOnly = true,
        [Description(ConfirmHelp)] bool confirm = false)
    {
        if (!confirm) return Task.FromResult(Preview(consistencyCheckOnly ? "Hardware consistency check" : "Compile hardware configuration (system data regenerated)", new { project, station }));
        return RunAsync(() => pcs7.InvokeAsync("s7_compile_station", Args(new { project, station, consistencyCheckOnly })));
    }

    [McpServerTool(Name = "s7_import_symbols", Destructive = true),
     Description("Imports symbols from an sdf/asc/dif/seq file into the symbol table of a program.")]
    public Task<string> ImportSymbols(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program,
        [Description(FileHelp + " symbol file")] string filePath,
        [Description("insert (only new symbols), overwrite-name or overwrite-operand")] string mode = "insert",
        [Description(ConfirmHelp)] bool confirm = false)
    {
        if (!confirm) return Task.FromResult(Preview("Import symbols into symbol table", new { project, program, filePath, mode }));
        return RunAsync(() => pcs7.InvokeAsync("s7_import_symbols", Args(new { project, program, filePath, mode })));
    }

    [McpServerTool(Name = "s7_import_station", Destructive = true),
     Description("Imports a station (racks, modules, parameters) from a .cfg file into a project.")]
    public Task<string> ImportStation(
        [Description("Project name or path")] string project,
        [Description(FileHelp + " .cfg file")] string filePath,
        [Description(ConfirmHelp)] bool confirm = false)
    {
        if (!confirm) return Task.FromResult(Preview("Import station from .cfg", new { project, filePath }));
        return RunAsync(() => pcs7.InvokeAsync("s7_import_station", Args(new { project, filePath })));
    }

    [McpServerTool(Name = "s7_set_object_properties", Destructive = true),
     Description("Changes comment, author or family (blocks only) of a block, source or chart. Omitted values are left unchanged.")]
    public Task<string> SetObjectProperties(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program,
        [Description("Folder kind: blocks, sources or charts")] string kind,
        [Description("Object name")] string name,
        [Description("New comment")] string? comment = null,
        [Description("New author")] string? author = null,
        [Description("New family (blocks only)")] string? family = null,
        [Description(ConfirmHelp)] bool confirm = false)
    {
        if (!confirm) return Task.FromResult(Preview("Change object properties", new { project, program, kind, name, comment, author, family }));
        return RunAsync(() => pcs7.InvokeAsync("s7_set_object_properties", Args(new { project, program, kind, name, comment, author, family })));
    }

    [McpServerTool(Name = "s7_save", Destructive = false, Idempotent = true),
     Description("Saves all pending changes made through the command interface.")]
    public Task<string> Save()
        => RunAsync(() => pcs7.InvokeAsync("s7_save", Args(new { })));
}
