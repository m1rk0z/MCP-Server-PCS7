using Newtonsoft.Json.Linq;
using Pcs7Core;
using Pcs7Core.Cfc;
using Pcs7Core.Operations;
using Xunit;

namespace Pcs7Tests;

/// <summary>Pcs7Core behaviour that does not depend on SIMATIC being installed.</summary>
public sealed class CoreTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "pcs7-tests", Guid.NewGuid().ToString("N"));

    private Pcs7Operations Ops(AccessMode mode) => new(new CoreOptions { AccessMode = mode, WorkDir = _work });

    private static bool SimaticInstalled => Type.GetTypeFromProgID("Simatic.Simatic") is not null;

    [Theory]
    [InlineData("s7_import_source")] [InlineData("s7_compile_source")] [InlineData("s7_compile_charts")]
    [InlineData("s7_compile_station")] [InlineData("s7_import_symbols")] [InlineData("s7_import_station")]
    [InlineData("s7_set_object_properties")] [InlineData("s7_save")] [InlineData("opc_write")]
    public async Task Read_only_mode_refuses_every_write_before_touching_PCS7(string op)
    {
        using var ops = Ops(AccessMode.ReadOnly);
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ops.InvokeAsync(op, new JObject { ["project"] = "x" }));
        Assert.Contains("read-only", ex.Message);
    }

    [Fact]
    public async Task Unknown_operations_are_refused()
    {
        using var ops = Ops(AccessMode.ReadWrite);
        await Assert.ThrowsAsync<ArgumentException>(() => ops.InvokeAsync("s7_download_to_plc", new JObject()));
    }

    [Fact]
    public void Download_to_PLC_and_CPU_control_do_not_exist()
    {
        foreach (var op in Pcs7Operations.ReadOperations.Concat(Pcs7Operations.WriteOperations))
            Assert.DoesNotMatch("download|delete|remove|start|stop|run|memory", op);
    }

    [Theory]
    [InlineData(@"..\agent.json")]
    [InlineData(@"..\..\Windows\win.ini")]
    [InlineData(@"C:\Windows\win.ini")]
    [InlineData(@"sub\..\..\x.txt")]
    public void Work_folder_paths_cannot_escape(string relative)
    {
        using var ops = Ops(AccessMode.ReadOnly);
        Assert.Throws<UnauthorizedAccessException>(() => ops.ResolveWorkFile(relative));
    }

    [Fact]
    public void Produced_files_are_only_existing_files_inside_the_work_folder()
    {
        using var ops = Ops(AccessMode.ReadOnly);
        var inside = Path.Combine(ops.Options.WorkDir, "P", "symbols", "S7.sdf");
        Directory.CreateDirectory(Path.GetDirectoryName(inside)!);
        File.WriteAllText(inside, "x");
        var outside = Path.Combine(Path.GetTempPath(), "pcs7-tests-outside.txt");
        File.WriteAllText(outside, "x");
        try
        {
            var result = new JObject
            {
                ["file"] = inside,
                ["nested"] = new JObject { ["log"] = outside, ["missing"] = Path.Combine(ops.Options.WorkDir, "none.txt") },
            };
            Assert.Equal(new[] { Path.Combine("P", "symbols", "S7.sdf") }, ops.ProducedFiles(result));
        }
        finally { File.Delete(outside); }
    }

    [Fact]
    public async Task Uploads_are_ignored_by_read_operations()
    {
        using var ops = Ops(AccessMode.ReadWrite);
        try { await ops.InvokeAsync("s7_object_details", Upload("x.scl")); } catch { } // fails later without PCS 7: irrelevant here
        Assert.False(Directory.Exists(Path.Combine(ops.Options.WorkDir, "_uploads")));
    }

    [Fact]
    public async Task Uploads_to_imports_are_stored_with_a_safe_name()
    {
        if (SimaticInstalled) return; // never run an import against a real SIMATIC installation
        using var ops = Ops(AccessMode.ReadWrite);
        try { await ops.InvokeAsync("s7_import_source", Upload(@"..\..\evil\FC1.scl")); } catch { }
        var file = Assert.Single(Directory.GetFiles(Path.Combine(ops.Options.WorkDir, "_uploads"), "*", SearchOption.AllDirectories));
        Assert.Equal("FC1.scl", Path.GetFileName(file));
        Assert.Equal("FUNCTION FC1", File.ReadAllText(file));
    }

    [Fact]
    public async Task Tool_arguments_satisfy_the_dispatcher()
    {
        if (SimaticInstalled) return; // the operations would run against the real projects
        using var ops = Ops(AccessMode.ReadWrite);
        foreach (var (method, attr) in ToolTests.Tools())
        {
            var op = attr.Name!;
            // OPC UA would wait for a server timeout; s7_save has no arguments.
            if (op.StartsWith("opc_") || op is "pcs7_status" or "s7_save") continue;
            var backend = new FakeBackend();
            ToolTests.Invoke(method, backend, confirm: true);
            var args = backend.Calls.Single().Args;
            var ex = await Record.ExceptionAsync(() => ops.InvokeAsync(op, args));
            Assert.NotNull(ex); // without PCS 7 the call fails...
            Assert.DoesNotContain("is required", ex!.Message); // ...but never because an argument name is wrong
        }
    }

    [Fact]
    public void Json_keeps_date_like_strings_and_offsets()
    {
        var o = (JObject)JsonUtil.Parse("{\"value\":\"2024-05-01T10:00:00\",\"ts\":\"2026-09-26T20:12:56.6659054+02:00\"}");
        var a = new Args(o);
        Assert.Equal("2024-05-01T10:00:00", a.OptStr("value"));
        Assert.Equal("2026-09-26T20:12:56.6659054+02:00", a.OptStr("ts"));
    }

    [Fact]
    public void Args_are_typed_and_required_ones_are_checked()
    {
        var a = new Args(JObject.Parse("{\"s\":\"v\",\"b\":\"true\",\"i\":5,\"arr\":[\"a\",null,\"b\"],\"n\":null}"));
        Assert.Equal("v", a.Str("s"));
        Assert.True(a.Bool("b"));
        Assert.Equal(5, a.Int("i", 0));
        Assert.Equal(7, a.Int("missing", 7));
        Assert.Equal(new[] { "a", "b" }, a.StrArray("arr"));
        Assert.Null(a.OptStr("n"));
        Assert.Contains("required", Assert.Throws<ArgumentException>(() => a.Str("n")).Message);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData(@"C:\Program Files\x", "\"C:\\Program Files\\x\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    public void Command_line_arguments_are_quoted_like_CommandLineToArgvW(string arg, string expected)
        => Assert.Equal(expected, CfcRunner.QuoteArg(arg));

    private static JObject Upload(string name) => new()
    {
        ["project"] = "x", ["program"] = "x", ["kind"] = "blocks", ["name"] = "x",
        ["filePath"] = name,
        ["filePath" + Pcs7Operations.UploadContentSuffix] = Convert.ToBase64String("FUNCTION FC1"u8.ToArray()),
    };

    public void Dispose()
    {
        try { Directory.Delete(_work, true); } catch { }
    }
}
