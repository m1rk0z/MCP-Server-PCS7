using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pcs7Mcp.Com;

namespace Pcs7Mcp.Tools;

internal static class ToolRunner
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Ok(object result) => JsonSerializer.Serialize(new { success = true, result }, Json);

    public static string Fail(Exception ex) => JsonSerializer.Serialize(new { success = false, error = ComObj.Describe(ex) }, Json);

    public static async Task<string> RunAsync(Func<Task<object>> func)
    {
        try { return Ok(await func()); }
        catch (Exception ex) { return Fail(ex); }
    }

    /// <summary>Two-step write: without confirm=true only the planned action is described.</summary>
    public static string Preview(string action, object details) => JsonSerializer.Serialize(new
    {
        success = true,
        preview = true,
        action,
        details,
        next = "Nothing was changed. Show this preview to the user and call the same tool again with confirm=true only after the user explicitly approves.",
    }, Json);
}
