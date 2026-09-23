using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Pcs7Mcp.Simatic;

namespace Pcs7Mcp.Tools;

/// <summary>
/// Read-only access to CFC chart contents (blocks, pin values, interconnections, tag connections).
/// The work is done by Pcs7CfcReader.exe (net48 x86, CFC database API s7jdbmox.dll) in a child process,
/// so a failure of the native Siemens DLL cannot take down the MCP server. The project is never modified.
/// </summary>
[McpServerToolType]
public sealed class CfcTools(SimaticSession s7, ServerOptions options)
{
    private static readonly string ReaderExe = Path.Combine(AppContext.BaseDirectory, "cfcreader", "Pcs7CfcReader.exe");

    [McpServerTool(Name = "cfc_list_charts", ReadOnly = true, Idempotent = true),
     Description("Lists the CFC charts of a PCS 7 project directly from the CFC database: chart name, comment, number of blocks and block types used. The CFC editor should be closed for this project.")]
    public Task<string> ListCharts(
        [Description("Project name or path (as returned by s7_list_projects)")] string project,
        [Description("Optional text filter on chart name")] string? filter = null)
        => ToolRunner.RunAsync(async () =>
        {
            var loc = await s7.RunAsync(() => s7.CfcLocation(project));
            var file = Path.Combine(loc.ExportDir, "cfc_list.json");
            var args = new List<string> { "list", "--project-dir", loc.ProjectDir, "--out", file };
            if (!string.IsNullOrWhiteSpace(filter)) { args.Add("--filter"); args.Add(filter); }
            await RunReader(args);
            return Inline(file);
        });

    [McpServerTool(Name = "cfc_read_charts", ReadOnly = true, Idempotent = true),
     Description("Reads the full content of one or more CFC charts: block instances (name, type, FB/FC, comment) and their pins with direction, data type, value, 'changed' flag (value differs from the block type default), block interconnections (from/to 'chart\\block.pin'), and connections to shared addresses (tag symbol/address). By default only changed or connected pins are returned.")]
    public Task<string> ReadCharts(
        [Description("Project name or path")] string project,
        [Description("Chart names, e.g. [\"7101-PT02\"]")] string[] charts,
        [Description("Return only pins with a changed value or a connection (default true). false = all pins incl. comments.")] bool changedOnly = true)
        => ToolRunner.RunAsync(async () =>
        {
            if (charts is null || charts.Length == 0) throw new ArgumentException("At least one chart name is required");
            var loc = await s7.RunAsync(() => s7.CfcLocation(project));
            var name = charts.Length == 1 ? SimaticSession.SanitizeFileName(charts[0]) : $"{charts.Length}_charts";
            var file = Path.Combine(loc.ExportDir, $"cfc_{name}.json");
            var args = new List<string> { "export", "--project-dir", loc.ProjectDir, "--charts", string.Join(",", charts), "--out", file };
            if (changedOnly) args.Add("--changed-only");
            await RunReader(args);
            return Inline(file);
        });

    [McpServerTool(Name = "cfc_export_charts", ReadOnly = true, Idempotent = true),
     Description("Exports the content of all (or filtered) CFC charts of a project to a JSON file in the export folder and returns only a summary (path, chart and block counts). Use it for large projects; read the file afterwards.")]
    public Task<string> ExportCharts(
        [Description("Project name or path")] string project,
        [Description("Optional text filter on chart name")] string? filter = null,
        [Description("Only pins with a changed value or a connection (default true)")] bool changedOnly = true)
        => ToolRunner.RunAsync(async () =>
        {
            var loc = await s7.RunAsync(() => s7.CfcLocation(project));
            var file = Path.Combine(loc.ExportDir, string.IsNullOrWhiteSpace(filter) ? "cfc_all.json" : $"cfc_filter_{SimaticSession.SanitizeFileName(filter)}.json");
            var args = new List<string> { "export", "--project-dir", loc.ProjectDir, "--out", file };
            if (!string.IsNullOrWhiteSpace(filter)) { args.Add("--filter"); args.Add(filter); }
            if (changedOnly) args.Add("--changed-only");
            var summary = await RunReader(args);
            return new { file, bytes = new FileInfo(file).Length, summary = JsonSerializer.Deserialize<JsonElement>(summary) };
        });

    private object Inline(string file)
    {
        var text = File.ReadAllText(file, Encoding.UTF8);
        if (text.Length > options.MaxInlineChars)
            return new { file, bytes = new FileInfo(file).Length, truncated = true, note = "Result too large to return inline; read the file or narrow the chart list." };
        return JsonSerializer.Deserialize<JsonElement>(text);
    }

    private static async Task<string> RunReader(IEnumerable<string> args)
    {
        if (!File.Exists(ReaderExe)) throw new FileNotFoundException("CFC reader not found", ReaderExe);
        var psi = new ProcessStartInfo(ReaderExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start " + ReaderExe);
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw new TimeoutException("CFC reader timed out"); }
        var err = (await stderr).Trim();
        if (p.ExitCode != 0) throw new InvalidOperationException($"CFC reader failed (exit {p.ExitCode}): {err}");
        return (await stdout).Trim();
    }
}
