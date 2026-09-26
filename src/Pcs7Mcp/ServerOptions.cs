using Pcs7Core;
using Pcs7Core.Operations;

namespace Pcs7Mcp;

public sealed class ServerOptions
{
    /// <summary>Which tools are exposed to the MCP client. In remote mode the agent must also allow writes.</summary>
    public AccessMode AccessMode { get; init; } = AccessMode.ReadOnly;

    /// <summary>Folder where exports, logs and generated files are written (override with --workdir or PCS7_MCP_WORKDIR).</summary>
    public string WorkDir { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pcs7-mcp", "export");

    /// <summary>Local mode only: in remote mode the agent uses its own OPC UA settings.</summary>
    public string OpcUaEndpoint { get; init; } = "opc.tcp://localhost:4863";

    /// <summary>URL of the Pcs7Agent on the PCS 7 machine (e.g. http://192.168.56.10:8765). Empty = local mode.</summary>
    public string? AgentUrl { get; init; }

    public string? AgentToken { get; init; }

    public TimeSpan AgentTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public bool IsRemote => !string.IsNullOrWhiteSpace(AgentUrl);

    public const string Usage =
        "Usage: Pcs7McpServer [--access-mode read-only|read-write] [--workdir <dir>] [--opcua-endpoint <url>]\n" +
        "                     [--agent <http://host:port> --agent-token <token> [--agent-timeout <minutes>]]";

    public static ServerOptions Parse(string[] args)
    {
        var mode = Environment.GetEnvironmentVariable("PCS7_MCP_ACCESS_MODE");
        var workDir = Environment.GetEnvironmentVariable("PCS7_MCP_WORKDIR");
        var endpoint = Environment.GetEnvironmentVariable("PCS7_MCP_OPCUA_ENDPOINT");
        var agent = Environment.GetEnvironmentVariable("PCS7_MCP_AGENT_URL");
        var token = Environment.GetEnvironmentVariable("PCS7_MCP_AGENT_TOKEN");
        var timeout = Environment.GetEnvironmentVariable("PCS7_MCP_AGENT_TIMEOUT");

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            switch (args[i].ToLowerInvariant())
            {
                case "--access-mode": mode = Next(); break;
                case "--workdir": workDir = Next(); break;
                case "--opcua-endpoint": endpoint = Next(); break;
                case "--agent": agent = Next(); break;
                case "--agent-token": token = Next(); break;
                case "--agent-timeout": timeout = Next(); break;
                default: throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        var defaults = new ServerOptions();
        if (!string.IsNullOrWhiteSpace(agent))
        {
            if (!agent.Contains("://")) agent = "http://" + agent;
            // The agent speaks plain HTTP only (use a private network, a VPN or an SSH tunnel).
            if (!Uri.TryCreate(agent, UriKind.Absolute, out var uri) || uri.Scheme != "http")
                throw new ArgumentException($"Invalid agent URL '{agent}' (expected http://host:port)");
            // No port in the URL: use the agent default port rather than 80.
            if (uri.IsDefaultPort && !agent.TrimEnd('/').EndsWith(":" + uri.Port)) agent = $"{uri.Scheme}://{uri.Host}:{AgentProtocol.DefaultPort}";
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("--agent-token (or PCS7_MCP_AGENT_TOKEN) is required with --agent");
        }

        return new ServerOptions
        {
            AccessMode = CoreOptions.ParseAccessMode(mode),
            WorkDir = string.IsNullOrWhiteSpace(workDir) ? defaults.WorkDir : workDir,
            OpcUaEndpoint = string.IsNullOrWhiteSpace(endpoint) ? defaults.OpcUaEndpoint : endpoint,
            AgentUrl = string.IsNullOrWhiteSpace(agent) ? null : agent.TrimEnd('/'),
            AgentToken = token?.Trim(),
            AgentTimeout = int.TryParse(timeout, out var min) && min > 0 ? TimeSpan.FromMinutes(min) : defaults.AgentTimeout,
        };
    }

    /// <summary>Settings for the in-process PCS 7 access (local mode).</summary>
    public CoreOptions ToCoreOptions() => new()
    {
        AccessMode = AccessMode,
        WorkDir = WorkDir,
        OpcUaEndpoint = OpcUaEndpoint,
        OpcUaUser = Environment.GetEnvironmentVariable("PCS7_MCP_OPCUA_USER"),
        OpcUaPassword = Environment.GetEnvironmentVariable("PCS7_MCP_OPCUA_PASSWORD"),
        OpcUaUseSecurity = !string.Equals(Environment.GetEnvironmentVariable("PCS7_MCP_OPCUA_SECURITY"), "none", StringComparison.OrdinalIgnoreCase),
    };
}
