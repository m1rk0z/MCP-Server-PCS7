using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Pcs7CfcReader
{
    /// <summary>
    /// Read-only CFC chart reader for PCS 7 / STEP 7 V5 projects.
    ///   Pcs7CfcReader list   --project-dir &lt;dir of .s7p&gt; [--out file.json]
    ///   Pcs7CfcReader export --project-dir &lt;dir&gt; [--charts a,b] [--filter text] [--changed-only] [--no-sinks] --out file.json
    /// A project can contain several CFC databases (ES_LOC\&lt;n&gt;, one per chart folder). Output is UTF-8 JSON.
    /// </summary>
    internal static class Program
    {
        private const string S7Bin = @"C:\Program Files (x86)\SIEMENS\STEP7\S7BIN";

        [STAThread]
        private static int Main(string[] args)
        {
            Environment.SetEnvironmentVariable("PATH", S7Bin + ";" + Environment.GetEnvironmentVariable("PATH"));
            Directory.SetCurrentDirectory(S7Bin);
            try
            {
                if (args.Length == 0) throw new ArgumentException("command required: list | export");
                var opts = Options(args.Skip(1).ToArray());
                string Opt(string k) => opts.TryGetValue(k, out var v) ? v : null;
                var projectDir = Opt("project-dir") ?? throw new ArgumentException("--project-dir is required");
                var dbs = Directory.Exists(Path.Combine(projectDir, "ES_LOC"))
                    ? Directory.GetDirectories(Path.Combine(projectDir, "ES_LOC")).Where(d => File.Exists(Path.Combine(d, "UNIT", "DOTS00.DAT"))).OrderBy(d => d).ToList()
                    : new List<string>();
                if (dbs.Count == 0) throw new InvalidOperationException("No CFC database (ES_LOC\\<n>\\UNIT) found in " + projectDir);

                var charts = Opt("charts")?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToList();
                var filter = Opt("filter");
                var command = args[0].ToLowerInvariant();
                var json = new Json();
                json.BeginObject();
                json.Prop("projectDir", projectDir);
                json.Prop("readAt", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
                json.Key("databases"); json.BeginArray();
                int totalCharts = 0, totalBlocks = 0;
                using (var db = new CfcDb())
                {
                    foreach (var dbPath in dbs)
                    {
                        json.BeginObject();
                        json.Prop("path", dbPath);
                        ulong project;
                        try { project = db.ProjectId(dbPath); }
                        catch (Exception ex) { json.Prop("error", ex.Message); json.EndObject(); continue; }
                        json.Key("chartFolders"); json.BeginArray();
                        foreach (var cpu in db.Cpus(project))
                        {
                            json.BeginObject();
                            json.Prop("name", cpu.Name);
                            json.Key("charts"); json.BeginArray();
                            foreach (var chart in db.Charts(cpu.Id))
                            {
                                if (charts != null && !charts.Any(c => string.Equals(c, chart.Name, StringComparison.OrdinalIgnoreCase))) continue;
                                if (filter != null && chart.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                                totalCharts++;
                                json.BeginObject();
                                json.Prop("name", chart.Name);
                                json.Prop("comment", db.Comment(chart.Id));
                                var objects = db.Objects(chart.Id);
                                totalBlocks += objects.Count;
                                if (command == "list")
                                {
                                    json.Prop("blockCount", objects.Count);
                                    json.Prop("blockTypes", string.Join(",", objects.Select(o => o.Type).Distinct()));
                                }
                                else
                                {
                                    json.Key("blocks"); json.BeginArray();
                                    foreach (var o in objects) WriteBlock(json, db, o, opts.ContainsKey("changed-only"), !opts.ContainsKey("no-sinks"));
                                    json.EndArray();
                                }
                                json.EndObject();
                            }
                            json.EndArray();
                            json.EndObject();
                        }
                        json.EndArray();
                        json.EndObject();
                    }
                }
                json.EndArray();
                json.Prop("chartCount", totalCharts);
                json.Prop("blockCount", totalBlocks);
                json.EndObject();

                var output = Opt("out");
                if (output == null) Console.Out.Write(json.ToString());
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                    File.WriteAllText(output, json.ToString(), new UTF8Encoding(false));
                    Console.Out.Write($"{{\"out\":{Json.Quote(Path.GetFullPath(output))},\"chartCount\":{totalCharts},\"blockCount\":{totalBlocks}}}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static void WriteBlock(Json json, CfcDb db, CfcObject o, bool changedOnly, bool withSinks)
        {
            json.BeginObject();
            json.Prop("name", o.Name);
            json.Prop("type", o.Type);
            json.Prop("kind", o.Kind == 1 ? "FB" : o.Kind == 2 ? "FC" : o.Kind.ToString(CultureInfo.InvariantCulture));
            json.Prop("comment", o.Comment);
            json.Key("pins"); json.BeginArray();
            foreach (var p in db.Parameters(o.Id, withSinks))
            {
                bool interesting = p.Changed || p.Connected || p.TagAddress != null || p.TagSymbol != null || p.Source != null;
                if (changedOnly && !interesting) continue;
                if (p.IsStructure && !p.Connected && p.Source == null && p.TagAddress == null) continue;
                json.BeginObject();
                json.Prop("name", p.Name);
                json.Prop("dir", p.Direction);
                json.Prop("type", TypeName(p.TypeCode));
                if (p.Value != null) json.Prop("value", p.Value);
                if (p.Changed) json.Prop("changed", true);
                if (!p.Visible) json.Prop("hidden", true);
                if (p.Source != null) json.Prop("from", p.Source);
                if (p.TagAddress != null || p.TagSymbol != null)
                {
                    json.Key("tag"); json.BeginObject();
                    json.Prop("symbol", p.TagSymbol); json.Prop("address", p.TagAddress); json.Prop("comment", p.TagComment);
                    json.EndObject();
                }
                if (p.Sinks != null && p.Sinks.Count > 0) { json.Key("to"); json.BeginArray(); foreach (var s in p.Sinks) json.Value(s); json.EndArray(); }
                if (p.Comment != null && !changedOnly) json.Prop("comment", p.Comment);
                json.EndObject();
            }
            json.EndArray();
            json.EndObject();
        }

        private static string TypeName(int code)
        {
            switch (code)
            {
                case 0: return "STRUCT";
                case 1: return "BOOL";
                case 2: return "BYTE";
                case 3: return "CHAR";
                case 4: return "WORD";
                case 5: return "INT";
                case 6: return "DWORD";
                case 7: return "DINT";
                case 8: return "REAL";
                default: return "code" + code.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static Dictionary<string, string> Options(string[] args)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) throw new ArgumentException("Unexpected argument " + args[i]);
                var key = args[i].Substring(2);
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) d[key] = args[++i]; else d[key] = "true";
            }
            return d;
        }
    }

    /// <summary>Minimal streaming JSON writer (no external dependencies on .NET Framework).</summary>
    internal sealed class Json
    {
        private readonly StringBuilder sb = new StringBuilder();
        private readonly Stack<bool> first = new Stack<bool>();
        private bool afterKey;

        private void Separator()
        {
            if (afterKey) { afterKey = false; return; }
            if (first.Count > 0) { if (!first.Peek()) sb.Append(','); first.Pop(); first.Push(false); }
        }

        public void BeginObject() { Separator(); sb.Append('{'); first.Push(true); }
        public void EndObject() { sb.Append('}'); first.Pop(); }
        public void BeginArray() { Separator(); sb.Append('['); first.Push(true); }
        public void EndArray() { sb.Append(']'); first.Pop(); }
        public void Key(string k) { Separator(); sb.Append(Quote(k)).Append(':'); afterKey = true; }
        public void Value(string v) { Separator(); sb.Append(v == null ? "null" : Quote(v)); }
        public void Prop(string k, string v) { if (v == null) return; Key(k); sb.Append(Quote(v)); afterKey = false; }
        public void Prop(string k, int v) { Key(k); sb.Append(v.ToString(CultureInfo.InvariantCulture)); afterKey = false; }
        public void Prop(string k, bool v) { Key(k); sb.Append(v ? "true" : "false"); afterKey = false; }

        public static string Quote(string s)
        {
            var b = new StringBuilder(s.Length + 2).Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default: if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); break;
                }
            }
            return b.Append('"').ToString();
        }

        public override string ToString() => sb.ToString();
    }
}
