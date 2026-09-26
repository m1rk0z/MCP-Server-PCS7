using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Pcs7Core.Operations;

/// <summary>Typed access to the JSON arguments of an operation.</summary>
public sealed class Args
{
    private readonly JObject _o;

    public Args(JObject o) => _o = o;

    private JToken? Get(string name) =>
        _o.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var t) && t.Type != JTokenType.Null && t.Type != JTokenType.Undefined ? t : null;

    public string Str(string name)
    {
        var v = OptStr(name);
        if (string.IsNullOrWhiteSpace(v)) throw new ArgumentException($"Argument '{name}' is required");
        return v!;
    }

    public string? OptStr(string name) => Get(name) is { } t ? Convert.ToString(((JValue)t).Value, CultureInfo.InvariantCulture) : null;

    public bool Bool(string name, bool defaultValue = false) => Get(name) is { } t ? t.Value<bool>() : defaultValue;

    public int Int(string name, int defaultValue) => Get(name) is { } t ? t.Value<int>() : defaultValue;

    public string[] StrArray(string name) => Get(name) switch
    {
        null => Array.Empty<string>(),
        JArray arr => arr.Where(x => x.Type != JTokenType.Null).Select(x => x.ToString()).ToArray(),
        var t => new[] { t.ToString() },
    };

    public void Set(string name, string value) => _o[name] = value;

    public void Remove(string name) => _o.Remove(name);
}
