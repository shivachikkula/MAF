using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Maf.Agents.Api.Mcp;

public interface IMcpToolProvider
{
    Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns a long-lived MCP client connection to the knowledge-base MCP server and caches its tool list.
/// The connection is re-established transparently if it fails.
/// </summary>
public sealed class McpToolProvider(
    IOptions<McpOptions> options,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory) : IMcpToolProvider, IAsyncDisposable
{
    public const string HttpClientName = "mcp";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _logger = loggerFactory.CreateLogger<McpToolProvider>();
    private McpClient? _client;
    private IReadOnlyList<AITool>? _tools;
    private DateTimeOffset _expiresAt;

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken)
    {
        if (_tools is { } cached && timeProvider.GetUtcNow() < _expiresAt)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_tools is { } fresh && timeProvider.GetUtcNow() < _expiresAt)
            {
                return fresh;
            }

            try
            {
                _client ??= await ConnectAsync(cancellationToken);
                var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
                _tools = [.. tools];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "MCP server call failed; reconnecting");
                await ResetClientAsync();
                _client = await ConnectAsync(cancellationToken);
                _tools = [.. await _client.ListToolsAsync(cancellationToken: cancellationToken)];
            }

            _expiresAt = timeProvider.GetUtcNow().AddMinutes(options.Value.ToolCacheMinutes);
            _logger.LogInformation("Loaded {Count} MCP tools: {Tools}", _tools.Count, string.Join(", ", _tools.Select(t => t.Name)));
            return _tools;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<McpClient> ConnectAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = o.Endpoint,
            Name = "maf-knowledge-base",
            TransportMode = HttpTransportMode.StreamableHttp,
        };

        if (!string.IsNullOrEmpty(o.ApiKey))
        {
            transportOptions.AdditionalHeaders = new Dictionary<string, string> { [Maf.ServiceDefaults.ApiKeyOptions.HeaderName] = o.ApiKey };
        }

        var transport = new HttpClientTransport(transportOptions, httpClientFactory.CreateClient(HttpClientName), loggerFactory, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport, loggerFactory: loggerFactory, cancellationToken: cancellationToken);
    }

    private async Task ResetClientAsync()
    {
        if (_client is not null)
        {
            try
            {
                await _client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing MCP client");
            }

            _client = null;
        }

        _tools = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetClientAsync();
        _gate.Dispose();
    }
}

/// <summary>Readiness check: the agent service is only useful if it can reach the MCP server.</summary>
public sealed class McpHealthCheck(IMcpToolProvider tools) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var list = await tools.GetToolsAsync(cancellationToken);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy($"{list.Count} MCP tools available");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("MCP server unreachable", ex);
        }
    }
}
