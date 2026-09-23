namespace Pcs7Mcp;

public enum AccessMode { ReadOnly, ReadWrite }

public sealed class ServerOptions
{
    public AccessMode AccessMode { get; init; } = AccessMode.ReadOnly;

    /// <summary>Folder where exports, logs and generated files are written (override with --workdir or PCS7_MCP_WORKDIR).</summary>
    public string WorkDir { get; init; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pcs7-mcp", "export");

    public string OpcUaEndpoint { get; init; } = "opc.tcp://localhost:4863";

    /// <summary>Max characters of file content returned inline to the client.</summary>
    public int MaxInlineChars { get; init; } = 150_000;

    public static ServerOptions Parse(string[] args)
    {
        var mode = Environment.GetEnvironmentVariable("PCS7_MCP_ACCESS_MODE");
        var workDir = Environment.GetEnvironmentVariable("PCS7_MCP_WORKDIR");
        var endpoint = Environment.GetEnvironmentVariable("PCS7_MCP_OPCUA_ENDPOINT");

        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            switch (args[i].ToLowerInvariant())
            {
                case "--access-mode": mode = Next(); break;
                case "--workdir": workDir = Next(); break;
                case "--opcua-endpoint": endpoint = Next(); break;
                default: throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        var defaults = new ServerOptions();
        return new ServerOptions
        {
            AccessMode = mode?.Trim().ToLowerInvariant() switch
            {
                null or "" or "read-only" or "readonly" => AccessMode.ReadOnly,
                "read-write" or "readwrite" => AccessMode.ReadWrite,
                _ => throw new ArgumentException($"Invalid access mode '{mode}' (use read-only or read-write)")
            },
            WorkDir = string.IsNullOrWhiteSpace(workDir) ? defaults.WorkDir : workDir,
            OpcUaEndpoint = string.IsNullOrWhiteSpace(endpoint) ? defaults.OpcUaEndpoint : endpoint,
        };
    }
}
