using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Pcs7Core;
using Pcs7Core.Operations;

namespace Pcs7Agent;

/// <summary>Agent settings, stored in %ProgramData%\Pcs7Agent\agent.json (written by the installer).</summary>
public sealed class AgentConfig
{
    /// <summary>%ProgramData%\Pcs7Agent, or PCS7_AGENT_DATA (portable use, tests).</summary>
    public static readonly string DataDir =
        Environment.GetEnvironmentVariable("PCS7_AGENT_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Pcs7Agent");

    public static string ConfigPath => Path.Combine(DataDir, "agent.json");
    public static string LogDir => Path.Combine(DataDir, "logs");

    public int Port { get; set; } = AgentProtocol.DefaultPort;

    /// <summary>"+" = all interfaces (needs the URL reservation made by setup); "localhost" = local only (e.g. behind an SSH tunnel).</summary>
    public string ListenHost { get; set; } = "+";

    /// <summary>Shared secret the MCP server must send ("Authorization: Bearer ...").</summary>
    public string Token { get; set; } = "";

    /// <summary>read-only or read-write. Write operations are refused in read-only mode whatever the client asks.</summary>
    public string AccessMode { get; set; } = "read-only";

    /// <summary>Client IPv4 addresses allowed to connect. Empty = any (the token is still required).</summary>
    public List<string> AllowedClients { get; set; } = new();

    public string WorkDir { get; set; } = Path.Combine(DataDir, "export");

    public string OpcUaEndpoint { get; set; } = "opc.tcp://localhost:4863";
    public string? OpcUaUser { get; set; }
    public string? OpcUaPassword { get; set; }
    public bool OpcUaUseSecurity { get; set; } = true;

    public static bool Exists => File.Exists(ConfigPath);

    public static AgentConfig Load()
    {
        if (!Exists) throw new FileNotFoundException("Agent not configured: run setup.cmd as administrator.", ConfigPath);
        var cfg = JsonConvert.DeserializeObject<AgentConfig>(File.ReadAllText(ConfigPath, Encoding.UTF8)) ?? new AgentConfig();
        if (string.IsNullOrWhiteSpace(cfg.Token)) throw new InvalidOperationException("No token in " + ConfigPath + ": run setup.cmd again.");
        return cfg;
    }

    public static AgentConfig LoadOrDefault()
    {
        try { return Exists ? JsonConvert.DeserializeObject<AgentConfig>(File.ReadAllText(ConfigPath, Encoding.UTF8)) ?? new AgentConfig() : new AgentConfig(); }
        catch { return new AgentConfig(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented), new UTF8Encoding(false));
    }

    public CoreOptions ToCoreOptions() => new()
    {
        AccessMode = CoreOptions.ParseAccessMode(AccessMode),
        WorkDir = WorkDir,
        OpcUaEndpoint = OpcUaEndpoint,
        OpcUaUser = OpcUaUser,
        OpcUaPassword = OpcUaPassword,
        OpcUaUseSecurity = OpcUaUseSecurity,
    };

    public bool IsReadWrite => CoreOptions.ParseAccessMode(AccessMode) == Pcs7Core.AccessMode.ReadWrite;

    public static string NewToken()
    {
        var bytes = new byte[32];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return string.Concat(bytes.Select(b => b.ToString("x2")));
    }

    /// <summary>IPv4 addresses of this machine, to show in the client configuration.</summary>
    public static List<string> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString())
                .Where(a => !a.StartsWith("169.254."))
                .Distinct()
                .ToList();
        }
        catch { return new List<string>(); }
    }

    /// <summary>The block to paste in %USERPROFILE%\.claude.json on the Claude Code PC.</summary>
    public string ClientSnippet(string? address = null)
    {
        var host = address ?? LocalAddresses().FirstOrDefault() ?? Environment.MachineName;
        return
$@"""pcs7"": {{
  ""type"": ""stdio"",
  ""command"": ""C:\\percorso\\pcs7-mcp\\release\\Pcs7McpServer.exe"",
  ""args"": [""--access-mode"", ""{AccessMode}"", ""--agent"", ""http://{host}:{Port}""],
  ""env"": {{ ""PCS7_MCP_AGENT_TOKEN"": ""{Token}"" }}
}}";
    }
}
