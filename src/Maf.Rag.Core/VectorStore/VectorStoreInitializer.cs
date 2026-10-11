using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maf.Rag.Core.VectorStore;

/// <summary>
/// Ensures the collection exists at startup, retrying while Qdrant comes up
/// (common under docker-compose / Kubernetes where start order isn't guaranteed).
/// </summary>
internal sealed class VectorStoreInitializer(IVectorStore store, ILogger<VectorStoreInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await store.EnsureCollectionAsync(stoppingToken);
                logger.LogInformation("Vector store is ready");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Vector store not ready, retrying in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}
