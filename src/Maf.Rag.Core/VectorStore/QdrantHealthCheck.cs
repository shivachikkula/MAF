using Microsoft.Extensions.Diagnostics.HealthChecks;
using Qdrant.Client;

namespace Maf.Rag.Core.VectorStore;

public sealed class QdrantHealthCheck(QdrantClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await client.HealthAsync(cancellationToken);
            return HealthCheckResult.Healthy($"Qdrant {result.Version}");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Qdrant is unreachable.", ex);
        }
    }
}
