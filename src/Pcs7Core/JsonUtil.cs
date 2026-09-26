using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Pcs7Core;

/// <summary>
/// JSON parsing that keeps strings as they are: Newtonsoft's default turns ISO-8601-looking strings into DateTime
/// (and shifts time zones), which would alter values such as an OPC UA write value or a timestamp crossing PC and VM.
/// </summary>
public static class JsonUtil
{
    public static JToken Parse(string json) => Read(new StringReader(json));

    public static JToken Read(TextReader reader)
    {
        using var json = new JsonTextReader(reader) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double };
        return JToken.ReadFrom(json);
    }
}
