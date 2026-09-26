using System;

namespace Pcs7Core
{
    /// <summary>Diagnostic sink: stderr for the MCP server, a log file for the agent.</summary>
    public static class CoreLog
    {
        public static Action<string> Sink { get; set; } = msg => Console.Error.WriteLine("[pcs7] " + msg);

        public static void Write(string message)
        {
            try { Sink(message); } catch { }
        }
    }
}
