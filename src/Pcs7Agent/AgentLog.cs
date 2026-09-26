using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Pcs7Agent;

/// <summary>Daily log files in %ProgramData%\Pcs7Agent\logs, kept for 30 days.</summary>
public static class AgentLog
{
    private static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(AgentConfig.LogDir);
                var file = Path.Combine(AgentConfig.LogDir, $"agent-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch { }
    }

    public static void Cleanup()
    {
        try
        {
            foreach (var f in new DirectoryInfo(AgentConfig.LogDir).GetFiles("agent-*.log").Where(f => f.LastWriteTime < DateTime.Now.AddDays(-30)))
                f.Delete();
        }
        catch { }
    }
}
