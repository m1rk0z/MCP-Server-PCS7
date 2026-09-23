using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace Pcs7Mcp.OpcUa;

/// <summary>OPC UA client towards the OpenPCS 7 UA server (runtime process data).</summary>
public sealed class OpcUaSession : IAsyncDisposable
{
    private readonly ServerOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ApplicationConfiguration? _config;
    private ISession? _session;
    private string? _endpointUrl;

    public OpcUaSession(ServerOptions options) => _options = options;

    private async Task<ApplicationConfiguration> ConfigAsync()
    {
        if (_config is not null) return _config;
        var pki = Path.Combine(_options.WorkDir, "_opcua_pki");
        var config = new ApplicationConfiguration
        {
            ApplicationName = "PCS7 MCP Server",
            ApplicationUri = Utils.Format("urn:{0}:Pcs7McpServer", Utils.GetHostName()),
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.Combine(pki, "own"),
                    SubjectName = "CN=PCS7 MCP Server",
                },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(pki, "issuer") },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(pki, "trusted") },
                RejectedCertificateStore = new CertificateTrustList { StoreType = CertificateStoreType.Directory, StorePath = Path.Combine(pki, "rejected") },
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true,
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 30000 },
            ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
        };
        await config.Validate(ApplicationType.Client);
        config.CertificateValidator.CertificateValidation += (_, e) => e.Accept = true;

        var app = new ApplicationInstance { ApplicationName = config.ApplicationName, ApplicationType = ApplicationType.Client, ApplicationConfiguration = config };
        await app.CheckApplicationInstanceCertificatesAsync(false, null, CancellationToken.None);
        _config = config;
        return config;
    }

    private async Task<ISession> SessionAsync(string? endpointUrl)
    {
        var url = string.IsNullOrWhiteSpace(endpointUrl) ? _options.OpcUaEndpoint : endpointUrl!;
        if (_session is { Connected: true } && _endpointUrl == url) return _session;
        if (_session is not null) { try { await _session.CloseAsync(); } catch { } _session.Dispose(); _session = null; }

        var config = await ConfigAsync();
        var useSecurity = !string.Equals(Environment.GetEnvironmentVariable("PCS7_MCP_OPCUA_SECURITY"), "none", StringComparison.OrdinalIgnoreCase);
        EndpointDescription selected;
        try { selected = CoreClientUtils.SelectEndpoint(config, url, useSecurity, 15000); }
        catch when (useSecurity) { selected = CoreClientUtils.SelectEndpoint(config, url, false, 15000); }

        var endpoint = new ConfiguredEndpoint(null, selected, EndpointConfiguration.Create(config));
        var user = Environment.GetEnvironmentVariable("PCS7_MCP_OPCUA_USER");
        IUserIdentity identity = string.IsNullOrEmpty(user)
            ? new UserIdentity(new AnonymousIdentityToken())
            : new UserIdentity(user, System.Text.Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("PCS7_MCP_OPCUA_PASSWORD") ?? ""));

        _session = await Session.Create(config, endpoint, false, "pcs7-mcp", 60000, identity, null);
        _endpointUrl = url;
        return _session;
    }

    private async Task<T> WithSession<T>(string? endpointUrl, Func<ISession, Task<T>> func)
    {
        await _lock.WaitAsync();
        try { return await func(await SessionAsync(endpointUrl)); }
        finally { _lock.Release(); }
    }

    public Task<object> StatusAsync(string? endpointUrl) => WithSession<object>(endpointUrl, async s =>
    {
        var status = await s.ReadValueAsync(VariableIds.Server_ServerStatus);
        var ss = ExtensionObject.ToEncodeable(status.Value as ExtensionObject) as ServerStatusDataType;
        return new
        {
            endpoint = s.Endpoint.EndpointUrl,
            securityPolicy = s.Endpoint.SecurityPolicyUri,
            securityMode = s.Endpoint.SecurityMode.ToString(),
            state = ss?.State.ToString(),
            product = ss?.BuildInfo?.ProductName,
            version = ss?.BuildInfo?.SoftwareVersion,
            currentTime = ss?.CurrentTime,
            namespaces = s.NamespaceUris.ToArray(),
        };
    });

    public Task<object> BrowseAsync(string? endpointUrl, string? nodeId, int maxResults) => WithSession<object>(endpointUrl, async s =>
    {
        var start = string.IsNullOrWhiteSpace(nodeId) ? ObjectIds.ObjectsFolder : NodeId.Parse(nodeId);
        var browser = new Browser(s)
        {
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = (int)(NodeClass.Object | NodeClass.Variable | NodeClass.Method),
            MaxReferencesReturned = (uint)Math.Clamp(maxResults, 1, 5000),
        };
        var refs = await Task.Run(() => browser.Browse(start));
        var children = refs.Take(Math.Clamp(maxResults, 1, 5000)).Select(r => new
        {
            nodeId = ExpandedNodeId.ToNodeId(r.NodeId, s.NamespaceUris)?.ToString(),
            browseName = r.BrowseName.ToString(),
            displayName = r.DisplayName.Text,
            nodeClass = r.NodeClass.ToString(),
        }).ToList();
        return new { node = start.ToString(), count = children.Count, children };
    });

    public Task<object> ReadAsync(string? endpointUrl, IReadOnlyList<string> nodeIds) => WithSession<object>(endpointUrl, async s =>
    {
        var results = new List<object>();
        foreach (var id in nodeIds)
        {
            try
            {
                var dv = await s.ReadValueAsync(NodeId.Parse(id));
                results.Add(new
                {
                    nodeId = id,
                    value = dv.Value is Array a ? string.Join(", ", a.Cast<object>()) : dv.Value?.ToString(),
                    type = dv.WrappedValue.TypeInfo?.BuiltInType.ToString(),
                    status = StatusCode.LookupSymbolicId(dv.StatusCode.Code),
                    sourceTimestamp = dv.SourceTimestamp,
                });
            }
            catch (Exception ex) { results.Add(new { nodeId = id, error = ex.Message }); }
        }
        return new { results };
    });

    public Task<object> WriteAsync(string? endpointUrl, string nodeId, string value) => WithSession<object>(endpointUrl, async s =>
    {
        var node = NodeId.Parse(nodeId);
        var current = await s.ReadValueAsync(node);
        var builtIn = current.WrappedValue.TypeInfo?.BuiltInType ?? BuiltInType.String;
        object converted = builtIn switch
        {
            BuiltInType.Boolean => value.Trim() is "1" || bool.Parse(value is "0" ? "false" : value),
            BuiltInType.SByte => sbyte.Parse(value), BuiltInType.Byte => byte.Parse(value),
            BuiltInType.Int16 => short.Parse(value), BuiltInType.UInt16 => ushort.Parse(value),
            BuiltInType.Int32 => int.Parse(value), BuiltInType.UInt32 => uint.Parse(value),
            BuiltInType.Int64 => long.Parse(value), BuiltInType.UInt64 => ulong.Parse(value),
            BuiltInType.Float => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            BuiltInType.Double => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            _ => value,
        };
        var write = new WriteValue { NodeId = node, AttributeId = Attributes.Value, Value = new DataValue(new Variant(converted)) };
        var response = await s.WriteAsync(null, new WriteValueCollection { write }, CancellationToken.None);
        var code = response.Results[0];
        return new
        {
            nodeId,
            previousValue = current.Value?.ToString(),
            writtenValue = converted.ToString(),
            type = builtIn.ToString(),
            status = StatusCode.LookupSymbolicId(code.Code),
            success = StatusCode.IsGood(code),
        };
    });

    public async ValueTask DisposeAsync()
    {
        if (_session is not null) { try { await _session.CloseAsync(); } catch { } _session.Dispose(); }
    }
}
