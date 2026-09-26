using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Pcs7Core.Com;
using Pcs7Core.Operations;

namespace Pcs7Mcp.Tools;

internal static class ToolRunner
{
    private static string Serialize(object o) => JToken.FromObject(o, Pcs7Operations.Serializer).ToString(Formatting.Indented);

    public static string Ok(JToken result) => new JObject { ["success"] = true, ["result"] = result }.ToString(Formatting.Indented);

    public static string Fail(Exception ex) => new JObject { ["success"] = false, ["error"] = ComObj.Describe(ex) }.ToString(Formatting.Indented);

    public static async Task<string> RunAsync(Func<Task<JToken>> func)
    {
        try { return Ok(await func()); }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>Builds the argument object of an operation; null values are left out.</summary>
    public static JObject Args(object args) => JObject.FromObject(args, Pcs7Operations.Serializer);

    /// <summary>Two-step write: without confirm=true only the planned action is described.</summary>
    public static string Preview(string action, object details) => Serialize(new
    {
        success = true,
        preview = true,
        action,
        details,
        next = "Nothing was changed. Show this preview to the user and call the same tool again with confirm=true only after the user explicitly approves.",
    });
}
