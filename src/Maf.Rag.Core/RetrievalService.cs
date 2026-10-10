using Maf.Rag.Core.VectorStore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Rag.Core;

public sealed class RetrievalService(
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IVectorStore vectorStore,
    IOptions<RagOptions> options)
{
    private readonly RagOptions _options = options.Value;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Query);

        using var activity = RagTelemetry.ActivitySource.StartActivity("rag.search");
        var topK = Math.Clamp(request.TopK ?? _options.DefaultTopK, 1, 50);
        var minScore = Math.Clamp(request.MinScore ?? _options.DefaultMinScore, 0, 1);

        var vector = await embeddings.GenerateVectorAsync(request.Query, cancellationToken: cancellationToken);
        var hits = await vectorStore.SearchAsync(vector, topK, minScore, request.Source, request.DocumentId, cancellationToken);

        RagTelemetry.SearchHits.Record(hits.Count);
        activity?.SetTag("rag.hits", hits.Count);
        return hits;
    }
}
