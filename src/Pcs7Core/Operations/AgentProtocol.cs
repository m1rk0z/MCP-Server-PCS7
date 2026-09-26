namespace Pcs7Core.Operations;

/// <summary>
/// HTTP contract between Pcs7McpServer (client, on the Claude Code PC) and Pcs7Agent (on the PCS 7 machine).
///
///   GET  /api/health              agent and station information
///   POST /api/invoke/{operation}  body: JSON arguments; response: { success, result | error, files[] }
///   GET  /api/file?path={rel}     download a file of the agent work folder (exports, logs)
///
/// Every request carries "Authorization: Bearer {token}". The token is generated when the agent is installed.
/// </summary>
public static class AgentProtocol
{
    public const int Version = 1;
    public const int DefaultPort = 8765;
    public const string HealthPath = "/api/health";
    public const string InvokePath = "/api/invoke/";
    public const string FilePath = "/api/file";
    public const string VersionHeader = "X-Pcs7-Protocol";
}
