using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pcs7Core.Cfc;
using Pcs7Core.Com;
using Pcs7Core.OpcUa;
using Pcs7Core.Simatic;

namespace Pcs7Core.Operations;

/// <summary>
/// Executes a PCS 7 operation by name with JSON arguments. The operation names are the MCP tool names.
/// Used in-process by the MCP server (local mode) and behind HTTP by Pcs7Agent (remote mode).
/// Write operations are refused unless this component runs in read-write mode, whatever the caller asks.
/// </summary>
public sealed class Pcs7Operations : IDisposable
{
    public static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
    });

    public static readonly IReadOnlyCollection<string> WriteOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "s7_import_source", "s7_compile_source", "s7_compile_charts", "s7_compile_station",
        "s7_import_symbols", "s7_import_station", "s7_set_object_properties", "s7_save", "opc_write",
    };

    /// <summary>Suffix of an argument carrying the base64 content of the file named by the argument itself (remote upload).</summary>
    public const string UploadContentSuffix = "__content";

    /// <summary>Arguments that name a file to import.</summary>
    public static readonly IReadOnlyCollection<string> FileArguments = new[] { "filePath" };

    /// <summary>Operations that accept an uploaded file (all of them are write operations).</summary>
    public static readonly IReadOnlyCollection<string> UploadOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "s7_import_source", "s7_import_symbols", "s7_import_station",
    };

    private static readonly TimeSpan UploadRetention = TimeSpan.FromDays(7);

    private readonly CoreOptions _options;
    private readonly StaDispatcher _sta;
    private readonly SimaticSession _s7;
    private readonly OpcUaSession _opc;
    private readonly CfcRunner _cfc;

    public Pcs7Operations(CoreOptions options)
    {
        // Absolute, normalized: produced files are recognized by prefix.
        options.WorkDir = Path.GetFullPath(options.WorkDir);
        _options = options;
        Directory.CreateDirectory(options.WorkDir);
        _sta = new StaDispatcher();
        _s7 = new SimaticSession(_sta, options);
        _opc = new OpcUaSession(options);
        _cfc = new CfcRunner(_s7, options);
    }

    public CoreOptions Options => _options;

    public static bool IsKnown(string op) => ReadOperations.Contains(op) || WriteOperations.Contains(op);

    public static readonly IReadOnlyCollection<string> ReadOperations = new HashSet<string>(StringComparer.Ordinal)
    {
        "s7_list_projects", "s7_project_structure", "s7_list_objects", "s7_object_details", "s7_read_block_code",
        "s7_export_source", "s7_export_symbols", "s7_station_hardware", "s7_export_station",
        "s7_export_program_structure", "s7_cpu_state",
        "cfc_list_charts", "cfc_read_charts", "cfc_export_charts",
        "opc_status", "opc_browse", "opc_read",
    };

    public async Task<JToken> InvokeAsync(string op, JObject? args)
    {
        var a = new Args(args ?? new JObject());
        if (!IsKnown(op)) throw new ArgumentException($"Unknown operation '{op}'");
        if (WriteOperations.Contains(op) && _options.AccessMode != AccessMode.ReadWrite)
            throw new UnauthorizedAccessException(
                $"'{op}' is a write operation and this PCS 7 station runs in read-only mode. " +
                "Enable read-write mode on the PCS 7 machine to allow it.");

        if (UploadOperations.Contains(op)) MaterializeUploads(a);
        object result = await Dispatch(op, a);
        return result as JToken ?? JToken.FromObject(result, Serializer);
    }

    private Task<object> S7(Func<object> f) => _s7.RunAsync(f);

    private async Task<object> Dispatch(string op, Args a) => op switch
    {
        // ---- engineering, read
        "s7_list_projects" => await S7(() => _s7.ListProjects(a.OptStr("filter"), a.Bool("includeLibraries"), a.Bool("details"))),
        "s7_project_structure" => await S7(() => _s7.ProjectStructure(a.Str("project"))),
        "s7_list_objects" => await S7(() => _s7.ListItems(a.Str("project"), a.Str("program"), a.Str("kind"), a.OptStr("filter"), a.Int("offset", 0), a.Int("limit", 100))),
        "s7_object_details" => await S7(() => _s7.ItemDetails(a.Str("project"), a.Str("program"), a.Str("kind"), a.Str("name"))),
        "s7_read_block_code" => await S7(() => _s7.BlockSource(a.Str("project"), a.Str("program"), a.StrArray("blocks"), a.Bool("includeUsedBlocks"))),
        "s7_export_source" => await S7(() => _s7.ExportSource(a.Str("project"), a.Str("program"), a.Str("source"))),
        "s7_export_symbols" => await S7(() => _s7.ExportSymbols(a.Str("project"), a.Str("program"), a.OptStr("format") ?? "sdf")),
        "s7_station_hardware" => await S7(() => _s7.StationHardware(a.Str("project"), a.Str("station"), a.Int("maxDepth", 2))),
        "s7_export_station" => await S7(() => _s7.ExportStation(a.Str("project"), a.Str("station"))),
        "s7_export_program_structure" => await S7(() => _s7.ExportProgramStructure(a.Str("project"), a.Str("program"))),
        "s7_cpu_state" => await S7(() => _s7.CpuState(a.Str("project"), a.Str("program"))),

        // ---- CFC database, read
        "cfc_list_charts" => await _cfc.ListCharts(a.Str("project"), a.OptStr("filter")),
        "cfc_read_charts" => await _cfc.ReadCharts(a.Str("project"), a.StrArray("charts"), a.Bool("changedOnly", true)),
        "cfc_export_charts" => await _cfc.ExportCharts(a.Str("project"), a.OptStr("filter"), a.Bool("changedOnly", true)),

        // ---- OPC UA runtime, read
        "opc_status" => await _opc.StatusAsync(a.OptStr("endpoint")),
        "opc_browse" => await _opc.BrowseAsync(a.OptStr("endpoint"), a.OptStr("nodeId"), a.Int("maxResults", 200)),
        "opc_read" => await _opc.ReadAsync(a.OptStr("endpoint"), a.StrArray("nodeIds")),

        // ---- write (read-write mode only, checked above)
        "s7_import_source" => await S7(() => _s7.ImportSource(a.Str("project"), a.Str("program"), a.Str("filePath"), a.OptStr("sourceName"), a.Bool("overwrite"))),
        "s7_compile_source" => await S7(() => _s7.CompileSource(a.Str("project"), a.Str("program"), a.Str("source"))),
        "s7_compile_charts" => await S7(() => _s7.CompileCharts(a.Str("project"), a.Str("program"))),
        "s7_compile_station" => await S7(() => _s7.CompileStation(a.Str("project"), a.Str("station"), a.Bool("consistencyCheckOnly", true))),
        "s7_import_symbols" => await S7(() => _s7.ImportSymbols(a.Str("project"), a.Str("program"), a.Str("filePath"), a.OptStr("mode") ?? "insert")),
        "s7_import_station" => await S7(() => _s7.ImportStation(a.Str("project"), a.Str("filePath"))),
        "s7_set_object_properties" => await S7(() => _s7.SetItemProperties(a.Str("project"), a.Str("program"), a.Str("kind"), a.Str("name"), a.OptStr("comment"), a.OptStr("author"), a.OptStr("family"))),
        "s7_save" => await S7(() => _s7.Save()),
        "opc_write" => await _opc.WriteAsync(a.OptStr("endpoint"), a.Str("nodeId"), a.Str("value")),

        _ => throw new ArgumentException($"Unknown operation '{op}'"),
    };

    /// <summary>
    /// A file sent by a remote client arrives as base64 in "&lt;arg&gt;__content": it is written to the work folder
    /// and the argument is replaced with the local path, keeping the original file name.
    /// </summary>
    private void MaterializeUploads(Args a)
    {
        foreach (var name in FileArguments)
        {
            var content = a.OptStr(name + UploadContentSuffix);
            if (content is null) continue;
            var original = a.OptStr(name) ?? "upload.dat";
            var fileName = SimaticSession.SanitizeFileName(Path.GetFileName(original.Replace('/', '\\').Split('\\').Last()));
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "upload.dat";
            PruneUploads();
            var dir = Path.Combine(_options.WorkDir, "_uploads", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName);
            File.WriteAllBytes(path, Convert.FromBase64String(content));
            a.Set(name, path);
            a.Remove(name + UploadContentSuffix);
        }
    }

    private void PruneUploads()
    {
        try
        {
            var root = new DirectoryInfo(Path.Combine(_options.WorkDir, "_uploads"));
            if (!root.Exists) return;
            foreach (var d in root.GetDirectories().Where(d => d.CreationTime < DateTime.Now - UploadRetention))
                d.Delete(true);
        }
        catch (Exception ex) { CoreLog.Write("cannot prune uploads: " + ex.Message); }
    }

    /// <summary>
    /// Files under the work folder referenced by a result (exports, logs), as paths relative to the work folder.
    /// A remote client downloads them so that they can also be opened on the client machine.
    /// </summary>
    public List<string> ProducedFiles(JToken result)
    {
        var root = Path.GetFullPath(_options.WorkDir).TrimEnd('\\') + "\\";
        var list = new List<string>();
        IEnumerable<JToken> tokens = result is JContainer c ? c.DescendantsAndSelf() : new[] { result };
        foreach (var v in tokens.OfType<JValue>())
        {
            if (v.Type != JTokenType.String) continue;
            var s = (string?)v.Value;
            if (s is null || s.Length > 400 || !s.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (!File.Exists(s)) continue;
                var rel = s.Substring(root.Length);
                if (!list.Contains(rel, StringComparer.OrdinalIgnoreCase)) list.Add(rel);
            }
            catch { }
        }
        return list;
    }

    /// <summary>Resolves a path relative to the work folder, refusing anything outside it.</summary>
    public string ResolveWorkFile(string relative)
    {
        var root = Path.GetFullPath(_options.WorkDir).TrimEnd('\\') + "\\";
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path outside the work folder");
        return full;
    }

    public void Dispose()
    {
        _opc.Dispose();
        _sta.Dispose();
    }
}
