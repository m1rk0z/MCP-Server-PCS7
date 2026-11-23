using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pcs7Core.Simatic;

/// <summary>A block or data type declared by an STL/SCL source: what compiling the source creates or overwrites.</summary>
public sealed record DeclaredBlock(string Keyword, string Name, bool Symbolic);

/// <summary>One row of a symbol table.</summary>
public sealed record SymbolRow(string Name, string Operand, string DataType, string Comment);

/// <summary>A symbol of the file that collides with one already in the table.</summary>
public sealed record SymbolConflict(string Kind, SymbolRow Current, SymbolRow Incoming);

public sealed record SymbolDiff(List<SymbolRow> Added, List<SymbolConflict> Conflicts, int Unchanged);

/// <summary>
/// Reads, without SIMATIC, what a write would touch: the blocks a source declares and the symbols a file changes.
/// Compiling a source and importing symbols overwrite without asking, so this is how the server knows beforehand.
/// </summary>
public static class SourceDeclarations
{
    private static readonly Regex BlockComment = new(@"\(\*.*?\*\)", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineComment = new(@"//[^\r\n]*", RegexOptions.Compiled);

    // FUNCTION_BLOCK must come before FUNCTION. The name is a quoted symbol, an absolute address (FC 990, FB10)
    // or, in SCL, a bare symbol.
    private static readonly Regex Declaration = new(
        @"^[ \t]*(FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|ORGANIZATION_BLOCK|TYPE)[ \t]+" +
        @"(?:""(?<sym>[^""\r\n]+)""|(?<abs>SFB|SFC|UDT|FB|FC|DB|OB)[ \t]*(?<num>\d+)(?![\w])|(?<bare>[^\s:;(]+))",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex Absolute = new(@"^(SFB|SFC|UDT|FB|FC|DB|OB)\s*(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<DeclaredBlock> Parse(string sourceText)
    {
        var text = LineComment.Replace(BlockComment.Replace(sourceText ?? "", " "), "");
        var result = new List<DeclaredBlock>();
        foreach (Match m in Declaration.Matches(text))
        {
            var keyword = m.Groups[1].Value.ToUpperInvariant();
            DeclaredBlock d =
                m.Groups["sym"].Success ? new(keyword, m.Groups["sym"].Value.Trim(), true) :
                m.Groups["abs"].Success ? new(keyword, m.Groups["abs"].Value.ToUpperInvariant() + m.Groups["num"].Value, false) :
                new(keyword, m.Groups["bare"].Value, true);
            if (!result.Any(r => SameName(r.Name, d.Name))) result.Add(d);
        }
        return result;
    }

    /// <summary>"FC 990", "fc990" and "FC990" are the same block; symbols compare without case.</summary>
    public static string Normalize(string? name)
    {
        var n = (name ?? "").Trim().Trim('"');
        var m = Absolute.Match(n);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() + m.Groups[2].Value : n.ToUpperInvariant();
    }

    public static bool SameName(string? a, string? b) => Normalize(a).Length > 0 && Normalize(a) == Normalize(b);

    public static bool Listed(IEnumerable<string>? names, params string?[] candidates) =>
        names is not null && names.Any(n => candidates.Any(c => SameName(n, c)));

    // ------------------------------------------------------------------ symbol tables (.sdf)

    private static readonly Regex Quoted = new("\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>Rows of an .sdf symbol file: "name","operand","data type","comment", padded with blanks.</summary>
    public static List<SymbolRow> ParseSdf(string text)
    {
        var rows = new List<SymbolRow>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var f = Quoted.Matches(line).Cast<Match>().Select(m => m.Groups[1].Value.Trim()).ToList();
            if (f.Count < 2 || f[0].Length == 0) continue;
            rows.Add(new SymbolRow(f[0], Operand(f[1]), f.Count > 2 ? Operand(f[2]) : "", f.Count > 3 ? f[3] : ""));
        }
        return rows;
    }

    /// <summary>"FC    990" -> "FC 990": operands compare without the padding.</summary>
    public static string Operand(string operand) => Regex.Replace((operand ?? "").Trim(), @"\s+", " ").ToUpperInvariant();

    public static SymbolDiff Diff(IReadOnlyList<SymbolRow> current, IReadOnlyList<SymbolRow> incoming)
    {
        var byName = new Dictionary<string, SymbolRow>(StringComparer.OrdinalIgnoreCase);
        var byOperand = new Dictionary<string, SymbolRow>(StringComparer.Ordinal);
        foreach (var c in current)
        {
            byName[c.Name] = c;
            if (c.Operand.Length > 0) byOperand[c.Operand] = c;
        }

        var added = new List<SymbolRow>();
        var conflicts = new List<SymbolConflict>();
        int unchanged = 0;
        foreach (var i in incoming)
        {
            if (byName.TryGetValue(i.Name, out var sameName))
            {
                if (sameName.Operand == i.Operand && sameName.DataType == i.DataType && sameName.Comment == i.Comment) unchanged++;
                else conflicts.Add(new SymbolConflict(sameName.Operand == i.Operand ? "attributes" : "name", sameName, i));
            }
            else if (i.Operand.Length > 0 && byOperand.TryGetValue(i.Operand, out var sameOperand))
                conflicts.Add(new SymbolConflict("operand", sameOperand, i));
            else added.Add(i);
        }
        return new SymbolDiff(added, conflicts, unchanged);
    }
}
