using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pcs7Mcp;
using Pcs7Mcp.Com;
using Pcs7Mcp.OpcUa;
using Pcs7Mcp.Simatic;
using Pcs7Mcp.Tools;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

ServerOptions options;
try { options = ServerOptions.Parse(args); }
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Usage: Pcs7McpServer [--access-mode read-only|read-write] [--workdir <dir>] [--opcua-endpoint <url>]");
    return 2;
}

Directory.CreateDirectory(options.WorkDir);

// Command-line args are consumed above; don't let the host reinterpret them as configuration.
var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
// stdout is the MCP channel: all logging goes to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<StaDispatcher>();
builder.Services.AddSingleton<SimaticSession>();
builder.Services.AddSingleton<OpcUaSession>();

var mcp = builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "pcs7-mcp", Version = "1.0.0" })
    .WithStdioServerTransport()
    .WithTools<S7ReadTools>()
    .WithTools<CfcTools>()
    .WithTools<OpcUaReadTools>();

if (options.AccessMode == AccessMode.ReadWrite)
{
    mcp.WithTools<S7WriteTools>()
       .WithTools<OpcUaWriteTools>();
}

Console.Error.WriteLine($"PCS7 MCP server - access mode {options.AccessMode}, workdir {options.WorkDir}");
await builder.Build().RunAsync();
return 0;
