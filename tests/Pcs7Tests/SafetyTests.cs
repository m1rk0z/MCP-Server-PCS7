using Newtonsoft.Json.Linq;
using Pcs7Core.Operations;
using Pcs7Core.Simatic;
using Xunit;

namespace Pcs7Tests;

/// <summary>What a write would overwrite must be known before it runs: declarations in sources, changes in symbol files.</summary>
public class SafetyTests
{
    [Fact]
    public void Stl_source_declares_blocks_by_absolute_name()
    {
        var d = SourceDeclarations.Parse("FUNCTION FC 990 : VOID\r\nTITLE = test\r\nBEGIN\r\nEND_FUNCTION\r\n\r\nDATA_BLOCK DB5\r\nBEGIN\r\nEND_DATA_BLOCK\r\n");
        Assert.Equal(new[] { "FC990", "DB5" }, d.Select(x => x.Name));
        Assert.All(d, x => Assert.False(x.Symbolic));
        Assert.Equal("FUNCTION", d[0].Keyword);
    }

    [Fact]
    public void Symbolic_names_are_kept_quoted_or_bare()
    {
        var d = SourceDeclarations.Parse("FUNCTION_BLOCK \"Motor Control\"\nEND_FUNCTION_BLOCK\nFUNCTION Scale_Value : REAL\nEND_FUNCTION\nTYPE UDT 3\nEND_TYPE\nORGANIZATION_BLOCK OB 1\n");
        Assert.Equal(new[] { "Motor Control", "Scale_Value", "UDT3", "OB1" }, d.Select(x => x.Name));
        Assert.Equal(new[] { true, true, false, false }, d.Select(x => x.Symbolic));
        Assert.Equal("FUNCTION_BLOCK", d[0].Keyword);
    }

    [Fact]
    public void A_symbol_that_only_looks_absolute_stays_a_symbol()
    {
        var d = Assert.Single(SourceDeclarations.Parse("FUNCTION FC1_Motor : VOID\n"));
        Assert.Equal("FC1_Motor", d.Name);
        Assert.True(d.Symbolic);
    }

    [Fact]
    public void Commented_declarations_and_repeats_are_ignored()
    {
        var d = SourceDeclarations.Parse("// FUNCTION FC 1 : VOID\n(* DATA_BLOCK DB 2\n more *)\nFUNCTION FC 3 : VOID\n  x := 1; // FUNCTION_BLOCK FB 9\nEND_FUNCTION\nFUNCTION fc3 : VOID\n");
        Assert.Equal("FC3", Assert.Single(d).Name);
    }

    [Fact]
    public void A_source_without_declarations_gives_nothing()
    {
        Assert.Empty(SourceDeclarations.Parse("this is not a source"));
        Assert.Empty(SourceDeclarations.Parse(""));
    }

    [Theory]
    [InlineData("FC 990", "fc990", true)]
    [InlineData("FC990", "FC 99", false)]
    [InlineData("\"Motor\"", "motor", true)]
    [InlineData("", "", false)]
    public void Block_names_compare_without_blanks_and_case(string a, string b, bool same) =>
        Assert.Equal(same, SourceDeclarations.SameName(a, b));

    [Fact]
    public void Keep_safe_matches_the_absolute_name_or_the_symbol()
    {
        Assert.True(SourceDeclarations.Listed(new[] { "FC 990" }, "Scale", "FC990", null));
        Assert.True(SourceDeclarations.Listed(new[] { "scale" }, "Scale", "FC990"));
        Assert.False(SourceDeclarations.Listed(new[] { "FC 991" }, "Scale", "FC990"));
        Assert.False(SourceDeclarations.Listed(null, "Scale"));
    }

    private const string Table =
        "\"Start                   \",\"I       0.0 \",\"BOOL      \",\"start button\"\r\n" +
        "\"Scale                   \",\"FC    990   \",\"FC    990 \",\"\"\r\n";

    [Fact]
    public void Sdf_rows_are_read_without_padding()
    {
        var rows = SourceDeclarations.ParseSdf(Table);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new SymbolRow("Start", "I 0.0", "BOOL", "start button"), rows[0]);
        Assert.Equal("FC 990", rows[1].Operand);
    }

    [Fact]
    public void Symbol_diff_tells_new_symbols_from_conflicts()
    {
        var current = SourceDeclarations.ParseSdf(Table);
        var incoming = SourceDeclarations.ParseSdf(
            "\"Start\",\"I 0.0\",\"BOOL\",\"start button\"\n" +   // unchanged
            "\"Scale\",\"FC 991\",\"FC 991\",\"\"\n" +             // same name, other operand
            "\"Begin\",\"I 0.0\",\"BOOL\",\"\"\n" +                // same operand, other name
            "\"Stop\",\"I 0.1\",\"BOOL\",\"\"\n");                 // new
        var diff = SourceDeclarations.Diff(current, incoming);
        Assert.Equal(1, diff.Unchanged);
        Assert.Equal("Stop", Assert.Single(diff.Added).Name);
        Assert.Equal(new[] { "name", "operand" }, diff.Conflicts.Select(c => c.Kind));
        Assert.Equal("FC 990", diff.Conflicts[0].Current.Operand);
        Assert.Equal("Start", diff.Conflicts[1].Current.Name);
    }

    [Fact]
    public void Preview_operations_are_known_but_are_neither_tools_nor_writes()
    {
        var tools = ToolTests.Tools().Select(t => t.Attr.Name).ToHashSet();
        foreach (var op in Pcs7Operations.PreviewOperations)
        {
            Assert.True(Pcs7Operations.IsKnown(op));
            Assert.DoesNotContain(op, Pcs7Operations.WriteOperations);
            Assert.DoesNotContain(op, tools);
        }
    }

    [Theory]
    [InlineData("s7_import_source", "s7_preview_import_source")]
    [InlineData("s7_compile_source", "s7_preview_compile_source")]
    [InlineData("s7_import_symbols", "s7_preview_import_symbols")]
    public void Preview_asks_what_would_be_replaced_and_never_writes(string tool, string previewOp)
    {
        var method = ToolTests.Tools().Single(t => t.Attr.Name == tool).Method;
        var backend = new FakeBackend();
        var result = JObject.Parse(ToolTests.Invoke(method, backend, confirm: false));
        Assert.True((bool)result["preview"]!);
        Assert.Equal(previewOp, Assert.Single(backend.Calls).Op);
        Assert.NotNull(result["wouldDo"]);
        Assert.Contains("keepSafe", backend.Calls[0].Args.Properties().Select(p => p.Name));
    }

    [Fact]
    public void A_preview_that_cannot_read_the_project_still_answers()
    {
        var method = ToolTests.Tools().Single(t => t.Attr.Name == "s7_compile_source").Method;
        var result = JObject.Parse(ToolTests.Invoke(method, new FailingBackend(), confirm: false));
        Assert.True((bool)result["preview"]!);
        Assert.Contains("unreachable", (string)result["planError"]!);
    }

    private sealed class FailingBackend : Pcs7Mcp.Backend.IPcs7Backend
    {
        public Task<JToken> InvokeAsync(string operation, JObject args) => throw new InvalidOperationException("unreachable");
        public Task<JToken> StatusAsync() => throw new InvalidOperationException("unreachable");
    }
}
