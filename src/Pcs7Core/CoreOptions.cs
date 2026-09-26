using System;
using System.IO;

namespace Pcs7Core
{
    public enum AccessMode { ReadOnly, ReadWrite }

    /// <summary>Settings of the component that actually talks to PCS 7 (local server or remote agent).</summary>
    public sealed class CoreOptions
    {
        public AccessMode AccessMode { get; set; } = AccessMode.ReadOnly;

        /// <summary>Folder where exports, logs and generated files are written.</summary>
        public string WorkDir { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pcs7-mcp", "export");

        public string OpcUaEndpoint { get; set; } = "opc.tcp://localhost:4863";
        public string? OpcUaUser { get; set; }
        public string? OpcUaPassword { get; set; }

        /// <summary>false = connect only to unsecured OPC UA endpoints (PCS7_MCP_OPCUA_SECURITY=none).</summary>
        public bool OpcUaUseSecurity { get; set; } = true;

        /// <summary>Max characters of file content returned inline to the client.</summary>
        public int MaxInlineChars { get; set; } = 150_000;

        public static AccessMode ParseAccessMode(string? mode) => (mode ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "read-only" or "readonly" => AccessMode.ReadOnly,
            "read-write" or "readwrite" => AccessMode.ReadWrite,
            _ => throw new ArgumentException($"Invalid access mode '{mode}' (use read-only or read-write)")
        };

        public static string AccessModeName(AccessMode mode) => mode == AccessMode.ReadWrite ? "read-write" : "read-only";
    }
}
