using System.ComponentModel;
using ModelContextProtocol.Server;
using Pcs7Mcp.Simatic;

namespace Pcs7Mcp.Tools;

[McpServerToolType]
public sealed class S7ReadTools(SimaticSession s7)
{
    [McpServerTool(Name = "s7_list_projects", ReadOnly = true, Idempotent = true),
     Description("Lists the projects, multiprojects and libraries registered in SIMATIC Manager (PCS 7 / STEP 7 V5.7).")]
    public Task<string> ListProjects(
        [Description("Optional text filter on project name or path")] string? filter = null,
        [Description("Include libraries (default false)")] bool includeLibraries = false,
        [Description("Also read author, comment, modification date and PCS 7 flag (slower: opens each project; combine with a filter)")] bool details = false)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.ListProjects(filter, includeLibraries, details)));

    [McpServerTool(Name = "s7_project_structure", ReadOnly = true, Idempotent = true),
     Description("Shows stations and S7 programs of a project, with the block/source/chart folders and item counts.")]
    public Task<string> ProjectStructure(
        [Description("Project name or project path (as returned by s7_list_projects)")] string project)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.ProjectStructure(project)));

    [McpServerTool(Name = "s7_list_objects", ReadOnly = true, Idempotent = true),
     Description("Lists blocks, sources or CFC/SFC charts of an S7 program, with type, symbol, language, author, comment and modification date. Paged.")]
    public Task<string> ListObjects(
        [Description("Project name or path")] string project,
        [Description("Program name (e.g. 'S7 Program') or its LogPath")] string program,
        [Description("Folder kind: blocks, sources or charts")] string kind,
        [Description("Optional filter on name or symbolic name")] string? filter = null,
        [Description("Items to skip (paging)")] int offset = 0,
        [Description("Max items to return (1-500, default 100)")] int limit = 100)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.ListItems(project, program, kind, filter, offset, limit)));

    [McpServerTool(Name = "s7_object_details", ReadOnly = true, Idempotent = true),
     Description("Returns the properties of a single block, source or chart.")]
    public Task<string> ObjectDetails(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program,
        [Description("Folder kind: blocks, sources or charts")] string kind,
        [Description("Object name (e.g. FB100, a source name or a chart name)")] string name)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.ItemDetails(project, program, kind, name)));

    [McpServerTool(Name = "s7_read_block_code", ReadOnly = true, Idempotent = true),
     Description("Reads the code of one or more offline blocks by generating an STL source file (GenerateSource). The project is not modified; the file is written to the export folder and its content returned.")]
    public Task<string> ReadBlockCode(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program,
        [Description("Block names, e.g. [\"FB100\", \"DB1\"]")] string[] blocks,
        [Description("Also include the blocks called by these blocks")] bool includeUsedBlocks = false)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.BlockSource(project, program, blocks, includeUsedBlocks)));

    [McpServerTool(Name = "s7_export_source", ReadOnly = true, Idempotent = true),
     Description("Exports an STL/SCL/GRAPH source of the program to a file and returns its content.")]
    public Task<string> ExportSource(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program,
        [Description("Source name")] string source)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.ExportSource(project, program, source)));

    [McpServerTool(Name = "s7_export_symbols", ReadOnly = true, Idempotent = true),
     Description("Exports the symbol table of a program (sdf, asc, dif or seq) and returns its content.")]
    public Task<string> ExportSymbols(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program,
        [Description("File format: sdf (default), asc, dif, seq")] string format = "sdf")
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.ExportSymbols(project, program, format)));

    [McpServerTool(Name = "s7_station_hardware", ReadOnly = true, Idempotent = true),
     Description("Reads the hardware configuration of a station: racks, modules, order numbers (MLFB), firmware versions and I/O addresses.")]
    public Task<string> StationHardware(
        [Description("Project name or path")] string project,
        [Description("Station name")] string station,
        [Description("Submodule depth (1-4, default 2)")] int maxDepth = 2)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.StationHardware(project, station, maxDepth)));

    [McpServerTool(Name = "s7_export_station", ReadOnly = true, Idempotent = true),
     Description("Exports a station hardware configuration to a .cfg file (HW Config export format) and returns its content.")]
    public Task<string> ExportStation(
        [Description("Project name or path")] string project,
        [Description("Station name")] string station)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.ExportStation(project, station)));

    [McpServerTool(Name = "s7_export_program_structure", ReadOnly = true, Idempotent = true),
     Description("Exports the block call structure of a program (DIF file) and returns its content.")]
    public Task<string> ExportProgramStructure(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.ExportProgramStructure(project, program)));

    [McpServerTool(Name = "s7_cpu_state", ReadOnly = true, Idempotent = true),
     Description("Reads online the operating state (RUN/STOP/...) of the CPU assigned to a program. Requires an online connection to the PLC; may take time if the PLC is unreachable.")]
    public Task<string> CpuState(
        [Description("Project name or path")] string project,
        [Description("Program name or LogPath")] string program)
        => ToolRunner.RunAsync(() => s7.RunAsync(() => s7.CpuState(project, program)));
}
