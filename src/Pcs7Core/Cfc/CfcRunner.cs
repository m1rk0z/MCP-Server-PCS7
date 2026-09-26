using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Pcs7Core.Simatic;

namespace Pcs7Core.Cfc;

/// <summary>
/// Read-only access to CFC chart contents (blocks, pin values, interconnections, tag connections).
/// The work is done by Pcs7CfcReader.exe (net48 x86, CFC database API s7jdbmox.dll) in a child process,
/// so a failure of the native Siemens DLL cannot take down the server. The project is never modified.
/// </summary>
public sealed class CfcRunner
{
    private static readonly string ReaderExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cfcreader", "Pcs7CfcReader.exe");
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(20);

    private readonly SimaticSession _s7;
    private readonly CoreOptions _options;

    public CfcRunner(SimaticSession s7, CoreOptions options)
    {
        _s7 = s7;
        _options = options;
    }

    public async Task<object> ListCharts(string project, string? filter)
    {
        var loc = await _s7.RunAsync(() => _s7.CfcLocation(project));
        var file = Path.Combine(loc.ExportDir, "cfc_list.json");
        var args = new List<string> { "list", "--project-dir", loc.ProjectDir, "--out", file };
        if (!string.IsNullOrWhiteSpace(filter)) { args.Add("--filter"); args.Add(filter!); }
        await RunReader(args);
        return Inline(file);
    }

    public async Task<object> ReadCharts(string project, string[] charts, bool changedOnly)
    {
        if (charts is null || charts.Length == 0) throw new ArgumentException("At least one chart name is required");
        var loc = await _s7.RunAsync(() => _s7.CfcLocation(project));
        var name = charts.Length == 1 ? SimaticSession.SanitizeFileName(charts[0]) : $"{charts.Length}_charts";
        var file = Path.Combine(loc.ExportDir, $"cfc_{name}.json");
        var args = new List<string> { "export", "--project-dir", loc.ProjectDir, "--charts", string.Join(",", charts), "--out", file };
        if (changedOnly) args.Add("--changed-only");
        await RunReader(args);
        return Inline(file);
    }

    public async Task<object> ExportCharts(string project, string? filter, bool changedOnly)
    {
        var loc = await _s7.RunAsync(() => _s7.CfcLocation(project));
        var file = Path.Combine(loc.ExportDir, string.IsNullOrWhiteSpace(filter) ? "cfc_all.json" : $"cfc_filter_{SimaticSession.SanitizeFileName(filter!)}.json");
        var args = new List<string> { "export", "--project-dir", loc.ProjectDir, "--out", file };
        if (!string.IsNullOrWhiteSpace(filter)) { args.Add("--filter"); args.Add(filter!); }
        if (changedOnly) args.Add("--changed-only");
        var summary = await RunReader(args);
        return new { file, bytes = new FileInfo(file).Length, summary = JsonUtil.Parse(summary) };
    }

    private object Inline(string file)
    {
        var text = File.ReadAllText(file, Encoding.UTF8);
        if (text.Length > _options.MaxInlineChars)
            return new { file, bytes = new FileInfo(file).Length, truncated = true, note = "Result too large to return inline; read the file or narrow the chart list." };
        return JsonUtil.Parse(text);
    }

    private static async Task<string> RunReader(IEnumerable<string> args)
    {
        if (!File.Exists(ReaderExe)) throw new FileNotFoundException("CFC reader not found", ReaderExe);
        var psi = new ProcessStartInfo(ReaderExe, string.Join(" ", args.Select(QuoteArg)))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start " + ReaderExe);
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        var exited = await Task.Run(() => p.WaitForExit((int)Timeout.TotalMilliseconds));
        if (!exited)
        {
            try { p.Kill(); } catch { }
            throw new TimeoutException("CFC reader timed out");
        }
        p.WaitForExit(); // flush redirected streams
        var err = (await stderr).Trim();
        if (p.ExitCode != 0) throw new InvalidOperationException($"CFC reader failed (exit {p.ExitCode}): {err}");
        return (await stdout).Trim();
    }

    /// <summary>Command-line quoting compatible with CommandLineToArgvW (ProcessStartInfo.ArgumentList is not available on net48).</summary>
    public static string QuoteArg(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;
        var sb = new StringBuilder("\"");
        int backslashes = 0;
        foreach (var c in arg)
        {
            if (c == '\\') { backslashes++; continue; }
            if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); }
            else { sb.Append('\\', backslashes); sb.Append(c); }
            backslashes = 0;
        }
        sb.Append('\\', backslashes * 2);
        return sb.Append('"').ToString();
    }
}
