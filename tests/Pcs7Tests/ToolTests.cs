using System.Reflection;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;
using Pcs7Core.Operations;
using Pcs7Mcp.Backend;
using Pcs7Mcp.Tools;
using Xunit;

namespace Pcs7Tests;

/// <summary>MCP tool declarations: annotations and mapping to the operations executed by Pcs7Core.</summary>
public class ToolTests
{
    private static readonly Type[] ToolTypes =
    {
        typeof(S7ReadTools), typeof(S7WriteTools), typeof(CfcTools),
        typeof(OpcUaReadTools), typeof(OpcUaWriteTools), typeof(StatusTools),
    };

    public static IEnumerable<(MethodInfo Method, McpServerToolAttribute Attr)> Tools() =>
        ToolTypes.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Select(m => (m, m.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.Item2 is not null)
            .Select(x => (x.m, x.Item2!));

    public static TheoryData<string> ToolNames()
    {
        var data = new TheoryData<string>();
        foreach (var (_, a) in Tools()) data.Add(a.Name!);
        return data;
    }

    private static (MethodInfo Method, McpServerToolAttribute Attr) Tool(string name) => Tools().Single(t => t.Attr.Name == name);

    [Fact]
    public void All_27_tools_are_declared_once()
    {
        var names = Tools().Select(t => t.Attr.Name).ToList();
        Assert.Equal(27, names.Count);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Every_tool_is_an_operation_known_to_the_dispatcher()
    {
        foreach (var (_, a) in Tools().Where(t => t.Attr.Name != "pcs7_status"))
            Assert.True(Pcs7Operations.IsKnown(a.Name!), $"{a.Name} has no operation in Pcs7Operations");
        var toolNames = Tools().Select(t => t.Attr.Name).ToHashSet();
        foreach (var op in Pcs7Operations.ReadOperations.Concat(Pcs7Operations.WriteOperations))
            Assert.Contains(op, toolNames);
    }

    [Theory, MemberData(nameof(ToolNames))]
    public void Annotations_match_what_the_tool_does(string name)
    {
        var (_, a) = Tool(name);
        var writes = Pcs7Operations.WriteOperations.Contains(name);
        Assert.Equal(!writes, a.ReadOnly);
        Assert.Equal(writes, a.Destructive);   // every write can overwrite project data or act on the live process
        Assert.False(a.OpenWorld);             // only the local PCS 7 project / plant, never the internet
        if (!writes) Assert.True(a.Idempotent);
    }

    [Theory, MemberData(nameof(ToolNames))]
    public void All_four_hints_are_emitted_explicitly(string name)
    {
        var (method, _) = Tool(name);
        var tool = McpServerTool.Create(method, _ => Activator.CreateInstance(method.DeclaringType!, new FakeBackend())!);
        var ann = tool.ProtocolTool.Annotations;
        Assert.NotNull(ann);
        Assert.NotNull(ann!.ReadOnlyHint);
        Assert.NotNull(ann.DestructiveHint);
        Assert.NotNull(ann.IdempotentHint);
        Assert.NotNull(ann.OpenWorldHint);
    }

    [Fact]
    public void Write_tools_only_preview_without_confirm()
    {
        foreach (var (method, a) in Tools().Where(t => Pcs7Operations.WriteOperations.Contains(t.Attr.Name!)))
        {
            if (a.Name == "s7_save") continue; // no confirm step: it only saves changes already confirmed
            var backend = new FakeBackend();
            var result = Invoke(method, backend, confirm: false);
            Assert.Contains("\"preview\": true", result);
            Assert.DoesNotContain(backend.Calls, c => Pcs7Operations.WriteOperations.Contains(c.Op));
        }
    }

    [Theory, MemberData(nameof(ToolNames))]
    public void Tool_sends_its_own_operation_with_its_parameter_names(string name)
    {
        if (name == "pcs7_status") return;
        var (method, _) = Tool(name);
        var backend = new FakeBackend();
        Invoke(method, backend, confirm: true);
        var call = Assert.Single(backend.Calls);
        Assert.Equal(name, call.Op);
        var expected = method.GetParameters().Select(p => p.Name!).Where(p => p != "confirm").OrderBy(p => p);
        Assert.Equal(expected, call.Args.Properties().Select(p => p.Name).OrderBy(p => p));
    }

    /// <summary>Calls a tool with a dummy value for every parameter.</summary>
    internal static string Invoke(MethodInfo method, IPcs7Backend backend, bool confirm)
    {
        var target = Activator.CreateInstance(method.DeclaringType!, backend)!;
        var args = method.GetParameters().Select(p => p.Name == "confirm" ? confirm : Dummy(p.ParameterType)).ToArray();
        return ((Task<string>)method.Invoke(target, args)!).GetAwaiter().GetResult();
    }

    internal static object Dummy(Type t) =>
        t == typeof(string) ? "x" :
        t == typeof(string[]) ? new[] { "x" } :
        t == typeof(bool) ? true :
        t == typeof(int) ? 1 :
        throw new NotSupportedException(t.Name);
}

internal sealed class FakeBackend : IPcs7Backend
{
    public List<(string Op, JObject Args)> Calls { get; } = new();

    public Task<JToken> InvokeAsync(string operation, JObject args)
    {
        Calls.Add((operation, args));
        return Task.FromResult<JToken>(new JObject { ["ok"] = true });
    }

    public Task<JToken> StatusAsync() => Task.FromResult<JToken>(new JObject { ["mode"] = "fake" });
}
