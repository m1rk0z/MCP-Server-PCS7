using System.Text;
using Pcs7Mcp.Com;
using static Pcs7Mcp.Com.ComObj;

namespace Pcs7Mcp.Simatic;

/// <summary>
/// Wraps the SIMATIC Manager command interface ("Simatic.Simatic", STEP 7 V5.7 / PCS 7 V10).
/// Every public method marshals its work onto the STA thread.
/// </summary>
public sealed class SimaticSession
{
    private readonly StaDispatcher _sta;
    private readonly ServerOptions _options;
    private object? _simatic;

    public SimaticSession(StaDispatcher sta, ServerOptions options)
    {
        _sta = sta;
        _options = options;
    }

    public string WorkDir => _options.WorkDir;
    private string LogDir => Path.Combine(_options.WorkDir, "_logs");
    private string VerbLogPath => Path.Combine(LogDir, "simatic_verb.log");

    public Task<T> RunAsync<T>(Func<T> func, CancellationToken ct = default) => _sta.InvokeAsync(func, ct);

    // ------------------------------------------------------------------ core objects

    private object Simatic
    {
        get
        {
            if (_simatic is not null) return _simatic;
            var type = Type.GetTypeFromProgID("Simatic.Simatic")
                       ?? throw new InvalidOperationException("COM class 'Simatic.Simatic' not registered: is STEP 7 V5.7 / PCS 7 installed?");
            var obj = Activator.CreateInstance(type)!;
            Directory.CreateDirectory(LogDir);
            // No message boxes may ever be shown: nobody could acknowledge them.
            try { Set(obj, "UnattendedServerMode", true); } catch { }
            // Silent mode: compile and batch messages go to a log file instead of dialogs.
            try { Set(obj, "VerbLogFile", VerbLogPath); } catch { }
            _simatic = obj;
            return obj;
        }
    }

    public string Save()
    {
        Call(Simatic, "Save");
        return "Changes saved.";
    }

    // ------------------------------------------------------------------ resolution

    private sealed record ProjectEntry(object Obj, string? Name, string? Path, int Type);
    private List<ProjectEntry>? _projectCache;
    private DateTime _projectCacheTime;

    private List<ProjectEntry> ProjectEntries(bool refresh = false)
    {
        if (!refresh && _projectCache is not null && DateTime.UtcNow - _projectCacheTime < TimeSpan.FromMinutes(10))
            return _projectCache;
        _projectCache = Items(Get(Simatic, "Projects"))
            .Select(p => new ProjectEntry(p, Str(p, "Name"), Str(p, "LogPath"), TryGet<int>(p, "Type")))
            .ToList();
        _projectCacheTime = DateTime.UtcNow;
        return _projectCache;
    }

    public object FindProject(string project)
    {
        var key = project.Trim().TrimEnd('\\');
        foreach (var refresh in new[] { false, true })
        {
            var all = ProjectEntries(refresh);
            var byPath = all.Where(p => string.Equals(p.Path?.TrimEnd('\\'), key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byPath.Count == 1) return byPath[0].Obj;
            var byName = all.Where(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 1) return byName[0].Obj;
            if (byName.Count > 1)
                throw new ArgumentException($"Project name '{project}' is ambiguous. Use the project path instead: " +
                                            string.Join("; ", byName.Select(p => p.Path)));
        }
        throw new ArgumentException($"Project '{project}' not found. Use s7_list_projects to see available projects.");
    }

    public List<object> Programs(object project) => Items(Get(project, "Programs")).ToList();

    public object FindProgram(object project, string program)
    {
        var key = program.Trim();
        var matches = Programs(project)
            .Where(p => string.Equals(Str(p, "Name"), key, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Str(p, "LogPath"), key, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var distinct = matches.GroupBy(p => Str(p, "LogPath") ?? "").Select(g => g.First()).ToList();
        if (distinct.Count == 1) return distinct[0];
        if (distinct.Count > 1)
            throw new ArgumentException($"Program '{program}' is ambiguous. Use its LogPath: " +
                                        string.Join("; ", distinct.Select(p => Str(p, "LogPath"))));
        throw new ArgumentException($"Program '{program}' not found. Use s7_project_structure to see programs.");
    }

    public object? FindContainer(object program, int concreteType) =>
        Items(Get(program, "Next")).FirstOrDefault(c => TryGet<int>(c, "ConcreteType") == concreteType);

    public object RequireContainer(object program, string kind)
    {
        var type = S7Constants.ContainerTypeFromKind(kind);
        return FindContainer(program, type)
               ?? throw new ArgumentException($"Program '{Str(program, "Name")}' has no {S7Constants.ContainerKind(type)} folder.");
    }

    public object FindItem(object container, string name)
    {
        try
        {
            var item = Get(Get(container, "Next")!, "Item", name);
            if (item is not null) return item;
        }
        catch { }
        var match = Items(Get(container, "Next")).FirstOrDefault(i =>
            string.Equals(Str(i, "Name"), name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TryGet<string>(i, "SymbolicName"), name, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ArgumentException($"Object '{name}' not found in folder '{Str(container, "Name")}'.");
    }

    public object FindStation(object project, string station)
    {
        var stations = Items(Get(project, "Stations")).ToList();
        return stations.FirstOrDefault(s => string.Equals(Str(s, "Name"), station, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException($"Station '{station}' not found. Available: " +
                                              string.Join(", ", stations.Select(s => Str(s, "Name"))));
    }

    // ------------------------------------------------------------------ descriptions

    public static Dictionary<string, object?> DescribeProject(object p) => new()
    {
        ["name"] = Str(p, "Name"),
        ["type"] = S7Constants.ProjectType(TryGet<int>(p, "Type")),
        ["path"] = Str(p, "LogPath"),
        ["pcs7"] = (TryGet<int>(p, "CategoryFlag") & 1) == 1,
        ["author"] = Str(p, "Creator"),
        ["comment"] = Str(p, "Comment"),
        ["modified"] = TryGet<DateTime>(p, "Modified"),
    };

    public static Dictionary<string, object?> DescribeItem(object item)
    {
        var d = new Dictionary<string, object?>
        {
            ["name"] = Str(item, "Name"),
            ["path"] = Str(item, "LogPath"),
        };
        var swType = TryGet<int>(item, "Type");
        var concrete = TryGet<int>(item, "ConcreteType");
        switch (swType)
        {
            case S7Constants.SwBlock:
                d["kind"] = "block";
                d["blockType"] = S7Constants.BlockType(concrete);
                d["symbol"] = TryGet<string>(item, "SymbolicName");
                d["language"] = TryGet<string>(item, "Language");
                d["family"] = TryGet<string>(item, "Family");
                d["headerName"] = TryGet<string>(item, "HeaderName");
                d["headerVersion"] = TryGet<string>(item, "HeaderVersion");
                d["size"] = TryGet<int>(item, "Size");
                d["knowHowProtected"] = TryGet<bool>(item, "KnowHowProtection");
                break;
            case S7Constants.SwSource:
                d["kind"] = "source";
                d["sourceType"] = S7Constants.SourceType(concrete);
                d["size"] = TryGet<int>(item, "Size");
                break;
            case S7Constants.SwPlan:
                d["kind"] = "chart";
                d["chartType"] = S7Constants.PlanType(concrete);
                break;
            case S7Constants.SwContainer:
                d["kind"] = S7Constants.ContainerKind(concrete);
                break;
            default:
                d["kind"] = $"type({swType})";
                break;
        }
        d["author"] = Str(item, "Creator");
        d["comment"] = Str(item, "Comment");
        d["modified"] = TryGet<DateTime>(item, "Modified");
        return d;
    }

    // ------------------------------------------------------------------ read operations

    public object ListProjects(string? filter, bool includeLibraries, bool details)
    {
        // Name/Type/LogPath are cheap; the other properties open each project and are slow.
        var list = ProjectEntries(refresh: true)
            .Select(p => (obj: p.Obj, name: p.Name, path: p.Path, type: S7Constants.ProjectType(p.Type)))
            .Where(p => includeLibraries || p.type != "Library")
            .Where(p => string.IsNullOrWhiteSpace(filter)
                        || p.name?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
                        || p.path?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
            .Select(p => details
                ? DescribeProject(p.obj)
                : new Dictionary<string, object?> { ["name"] = p.name, ["type"] = p.type, ["path"] = p.path })
            .ToList();
        return new { count = list.Count, projects = list };
    }

    public object ProjectStructure(string project)
    {
        var p = FindProject(project);
        var stations = Items(Get(p, "Stations")).Select(s => new
        {
            name = Str(s, "Name"),
            type = TryGet<int>(s, "Type"),
        }).ToList();

        var programs = Programs(p)
            .GroupBy(pr => Str(pr, "LogPath") ?? "")
            .Select(g => g.First())
            .Select(pr => new
            {
                name = Str(pr, "Name"),
                path = Str(pr, "LogPath"),
                folders = Items(Get(pr, "Next")).Select(c => new
                {
                    name = Str(c, "Name"),
                    kind = S7Constants.ContainerKind(TryGet<int>(c, "ConcreteType")),
                    count = TryGet<int>(Get(c, "Next")!, "Count"),
                }).ToList(),
            })
            .ToList();

        return new { project = DescribeProject(p), stations, programs };
    }

    public object ListItems(string project, string program, string kind, string? filter, int offset, int limit)
    {
        var p = FindProject(project);
        var pr = FindProgram(p, program);
        var container = RequireContainer(pr, kind);
        IEnumerable<object> all = Items(Get(container, "Next"));
        if (!string.IsNullOrWhiteSpace(filter))
            all = all.Where(i => (Str(i, "Name") ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase)
                              || (TryGet<string>(i, "SymbolicName") ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase));
        var total = TryGet<int>(Get(container, "Next")!, "Count");
        var page = all.Skip(Math.Max(0, offset)).Take(Math.Clamp(limit, 1, 500)).Select(DescribeItem).ToList();
        return new { folder = Str(container, "Name"), totalInFolder = total, offset, returned = page.Count, items = page };
    }

    public object ItemDetails(string project, string program, string kind, string name)
    {
        var item = FindItem(RequireContainer(FindProgram(FindProject(project), program), kind), name);
        return DescribeItem(item);
    }

    public object StationHardware(string project, string station, int maxDepth)
    {
        var st = FindStation(FindProject(project), station);
        var racks = Items(Get(st, "Racks")).Select(r => new
        {
            name = Str(r, "Name"),
            index = TryGet<object>(r, "Index")?.ToString(),
            mlfb = Str(r, "MLFB"),
            version = Str(r, "Version"),
            modules = Modules(r, 1, Math.Clamp(maxDepth, 1, 4)),
        }).ToList();
        return new { station = Str(st, "Name"), type = TryGet<int>(st, "Type"), racks };
    }

    private static List<object> Modules(object parent, int depth, int maxDepth)
    {
        return Items(TryGet<object>(parent, "Modules")).Select(m => (object)new
        {
            name = Str(m, "Name"),
            slot = TryGet<object>(m, "Index")?.ToString(),
            mlfb = Str(m, "MLFB"),
            version = Str(m, "Version"),
            inputs = Addresses(m, "LocalInAddresses"),
            outputs = Addresses(m, "LocalOutAddresses"),
            submodules = depth < maxDepth ? Modules(m, depth + 1, maxDepth) : null,
        }).ToList();
    }

    private static List<object>? Addresses(object module, string prop)
    {
        var list = Items(TryGet<object>(module, prop)).Select(a => (object)new
        {
            address = TryGet<int>(a, "LogicalAddress"),
            length = TryGet<int>(a, "Length"),
            processImagePartition = TryGet<int>(a, "PartProcessImage"),
        }).ToList();
        return list.Count == 0 ? null : list;
    }

    public object CpuState(string project, string program)
    {
        var pr = FindProgram(FindProject(project), program);
        var state = Convert.ToInt32(Get(pr, "ModuleState"));
        return new { program = Str(pr, "LogPath"), state = S7Constants.ModuleState(state) };
    }

    // ------------------------------------------------------------------ exports (write files only, project unchanged)

    /// <summary>Project folder (contains ES_LOC with the CFC databases) and the export folder for CFC reads.</summary>
    public (string ProjectDir, string ExportDir, string Name) CfcLocation(string project)
    {
        var p = FindProject(project);
        var dir = Str(p, "LogPath") ?? throw new InvalidOperationException("Project has no LogPath");
        var name = Str(p, "Name") ?? "project";
        var export = Path.Combine(_options.WorkDir, Sanitize(name), "cfc");
        Directory.CreateDirectory(export);
        return (dir, export, name);
    }

    public static string SanitizeFileName(string name) => Sanitize(name);

    private string ExportPath(object project, string sub, string fileName)
    {
        var dir = Path.Combine(_options.WorkDir, Sanitize(Str(project, "Name") ?? "project"), sub);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Sanitize(fileName));
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    public object ExportSymbols(string project, string program, string format)
    {
        var p = FindProject(project);
        var pr = FindProgram(p, program);
        var ext = format.Trim().TrimStart('.').ToLowerInvariant();
        if (ext is not ("sdf" or "asc" or "dif" or "seq"))
            throw new ArgumentException("format must be sdf, asc, dif or seq");
        var file = ExportPath(p, "symbols", $"{Str(pr, "Name")}.{ext}");
        if (File.Exists(file)) File.Delete(file);
        Call(Get(pr, "SymbolTable")!, "Export", file);
        return FileResult(file);
    }

    public object BlockSource(string project, string program, IReadOnlyList<string> blocks, bool includeUsedBlocks)
    {
        var p = FindProject(project);
        var pr = FindProgram(p, program);
        var container = RequireContainer(pr, "blocks");
        var results = new List<object>();
        foreach (var name in blocks)
        {
            try
            {
                var blk = FindItem(container, name);
                var file = ExportPath(p, "block_sources", $"{Str(blk, "Name")}.awl");
                if (File.Exists(file)) File.Delete(file);
                int flags = S7Constants.GsfDoOverwrite | (includeUsedBlocks ? S7Constants.GsfIncludeUsedBlocks : 0);
                Call(blk, "GenerateSource", file, flags);
                results.Add(FileResult(file, name));
            }
            catch (Exception ex)
            {
                results.Add(new { block = name, error = Describe(ex) });
            }
        }
        return new { results };
    }

    public object ExportSource(string project, string program, string source)
    {
        var p = FindProject(project);
        var src = FindItem(RequireContainer(FindProgram(p, program), "sources"), source);
        var ext = S7Constants.SourceType(TryGet<int>(src, "ConcreteType")) switch
        {
            "SCL" or "SCL-Encrypted" => "scl",
            "SCL-Make" => "inp",
            "GRAPH" => "gr7",
            _ => "awl"
        };
        var file = ExportPath(p, "sources", $"{Str(src, "Name")}.{ext}");
        if (File.Exists(file)) File.Delete(file);
        Call(src, "Export", file);
        return FileResult(file);
    }

    public object ExportStation(string project, string station)
    {
        var p = FindProject(project);
        var st = FindStation(p, station);
        var file = ExportPath(p, "hardware", $"{Str(st, "Name")}.cfg");
        if (File.Exists(file)) File.Delete(file);
        Call(st, "Export", file);
        return FileResult(file);
    }

    public object ExportProgramStructure(string project, string program)
    {
        var p = FindProject(project);
        var pr = FindProgram(p, program);
        var file = ExportPath(p, "structure", $"{Str(pr, "Name")}_structure.dif");
        if (File.Exists(file)) File.Delete(file);
        // symbols | symbol comments | language | details
        Call(pr, "ExportProgramStructure", file, false, 1 | 2 | 4 | 8);
        return FileResult(file);
    }

    // ------------------------------------------------------------------ write operations

    public object ImportSource(string project, string program, string filePath, string? sourceName, bool overwrite)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Source file not found", filePath);
        var p = FindProject(project);
        var container = RequireContainer(FindProgram(p, program), "sources");
        var name = string.IsNullOrWhiteSpace(sourceName) ? Path.GetFileNameWithoutExtension(filePath) : sourceName!;
        object? existing = null;
        try { existing = FindItem(container, name); } catch (ArgumentException) { }
        if (existing is not null)
        {
            if (!overwrite) throw new InvalidOperationException($"Source '{name}' already exists. Set overwrite=true to replace it.");
            Call(existing, "Remove");
        }
        var added = Call(Get(container, "Next")!, "Add", name, S7Constants.SwSource, filePath)!;
        return new { imported = DescribeItem(added), replacedExisting = existing is not null };
    }

    public object CompileSource(string project, string program, string source)
    {
        var src = FindItem(RequireContainer(FindProgram(FindProject(project), program), "sources"), source);
        return WithVerbLog(() => Call(src, "Compile"));
    }

    public object CompileCharts(string project, string program)
    {
        var charts = RequireContainer(FindProgram(FindProject(project), program), "charts");
        return WithVerbLog(() => Call(charts, "Compile", 1));
    }

    public object CompileStation(string project, string station, bool consistencyCheckOnly)
    {
        var st = FindStation(FindProject(project), station);
        return WithVerbLog(() => Call(st, "Compile", consistencyCheckOnly ? 1 : 0));
    }

    public object ImportSymbols(string project, string program, string filePath, string mode)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Symbol file not found", filePath);
        var pr = FindProgram(FindProject(project), program);
        var count = Call(Get(pr, "SymbolTable")!, "Import", filePath, S7Constants.SymbolImportFlag(mode));
        return new { program = Str(pr, "LogPath"), result = count, mode };
    }

    public object ImportStation(string project, string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Station .cfg file not found", filePath);
        var p = FindProject(project);
        var st = Call(Get(p, "Stations")!, "Import", filePath);
        return new { imported = st is null ? null : Str(st, "Name") };
    }

    public object SetItemProperties(string project, string program, string kind, string name, string? comment, string? author, string? family)
    {
        var item = FindItem(RequireContainer(FindProgram(FindProject(project), program), kind), name);
        if (comment is not null) Set(item, "Comment", comment);
        if (author is not null) Set(item, "Creator", author);
        if (family is not null) Set(item, "Family", family);
        return DescribeItem(item);
    }

    private object WithVerbLog(Action action)
    {
        Directory.CreateDirectory(LogDir);
        try { if (File.Exists(VerbLogPath)) File.Delete(VerbLogPath); } catch { }
        Set(Simatic, "VerbLogFile", VerbLogPath);
        string? error = null;
        try { action(); }
        catch (Exception ex) { error = Describe(ex); }
        var log = File.Exists(VerbLogPath) ? ReadText(VerbLogPath) : "";
        return new { success = error is null, error, log, logFile = VerbLogPath };
    }

    // ------------------------------------------------------------------ files

    public object FileResult(string file, string? label = null)
    {
        if (!File.Exists(file)) return new { label, file, error = "The export did not produce a file." };
        var text = ReadText(file);
        var truncated = text.Length > _options.MaxInlineChars;
        return new
        {
            label,
            file,
            bytes = new FileInfo(file).Length,
            truncated,
            content = truncated ? text[.._options.MaxInlineChars] : text,
        };
    }

    public static string ReadText(string file)
    {
        var bytes = File.ReadAllBytes(file);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        // STEP 7 V5.7 writes some exports as UTF-8 without BOM, others as ANSI (Windows-1252).
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(bytes); }
    }
}
