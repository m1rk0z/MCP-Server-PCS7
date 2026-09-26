using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pcs7Core;
using Pcs7Mcp;
using Pcs7Mcp.Backend;
using Pcs7Mcp.Tools;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

ServerOptions options;
try { options = ServerOptions.Parse(args); }
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(ServerOptions.Usage);
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
if (options.IsRemote)
    builder.Services.AddSingleton<IPcs7Backend>(_ => new RemoteBackend(options));
else
    builder.Services.AddSingleton<IPcs7Backend>(_ => new LocalBackend(options));

var mcp = builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "pcs7-mcp", Version = "1.1.0" })
    .WithStdioServerTransport()
    .WithTools<StatusTools>()
    .WithTools<S7ReadTools>()
    .WithTools<CfcTools>()
    .WithTools<OpcUaReadTools>();

if (options.AccessMode == AccessMode.ReadWrite)
{
    mcp.WithTools<S7WriteTools>()
       .WithTools<OpcUaWriteTools>();
}

Console.Error.WriteLine(options.IsRemote
    ? $"PCS7 MCP server - access mode {options.AccessMode}, remote agent {options.AgentUrl}, local copies in {options.WorkDir}"
    : $"PCS7 MCP server - access mode {options.AccessMode}, local mode, workdir {options.WorkDir}");
await builder.Build().RunAsync();
return 0;
