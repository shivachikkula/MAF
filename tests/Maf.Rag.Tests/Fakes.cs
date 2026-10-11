using Maf.Rag.Core;
using Maf.Rag.Core.VectorStore;
using Microsoft.Extensions.AI;

namespace Maf.Rag.Tests;

/// <summary>Bag-of-letters embedding: deterministic, and similar texts get similar vectors.</summary>
internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public int Calls { get; private set; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        var result = new GeneratedEmbeddings<Embedding<float>>(values.Select(v => new Embedding<float>(Embed(v))));
        return Task.FromResult(result);
    }

    internal static float[] Embed(string text)
    {
        var v = new float[26];
        foreach (var c in text.ToUpperInvariant())
        {
            if (c is >= 'A' and <= 'Z')
            {
                v[c - 'A']++;
            }
        }

        var norm = MathF.Sqrt(v.Sum(x => x * x));
        return norm == 0 ? v : v.Select(x => x / norm).ToArray();
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class InMemoryVectorStore : IVectorStore
{
    public Dictionary<Guid, DocumentChunk> Points { get; } = [];

    public Task EnsureCollectionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ReplaceDocumentAsync(string documentId, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        foreach (var c in chunks)
        {
            Points[c.Id] = c;
        }

        foreach (var stale in Points.Values.Where(p => p.DocumentId == documentId && p.ChunkIndex >= chunks.Count).ToList())
        {
            Points.Remove(stale.Id);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(ReadOnlyMemory<float> queryVector, int topK, double minScore, string? source, string? documentId, CancellationToken cancellationToken)
    {
        var q = queryVector.ToArray();
        IReadOnlyList<SearchHit> hits = Points.Values
            .Where(p => source is null || p.Source == source)
            .Where(p => documentId is null || p.DocumentId == documentId)
            .Select(p => (p, score: (double)q.Zip(p.Embedding.ToArray(), (a, b) => a * b).Sum()))
            .Where(x => x.score >= minScore)
            .OrderByDescending(x => x.score)
            .Take(topK)
            .Select(x => new SearchHit(x.p.DocumentId, x.p.Source, x.p.Title, x.p.ChunkIndex, x.p.Text, x.score, x.p.Metadata))
            .ToList();
        return Task.FromResult(hits);
    }

    public Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(int limit, CancellationToken cancellationToken)
    {
        IReadOnlyList<DocumentSummary> docs = Points.Values.Where(p => p.ChunkIndex == 0)
            .Select(p => new DocumentSummary(p.DocumentId, p.Source, p.Title, p.TotalChunks, p.IngestedAt))
            .Take(limit).ToList();
        return Task.FromResult(docs);
    }

    public Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken)
    {
        var ids = Points.Values.Where(p => p.DocumentId == documentId).Select(p => p.Id).ToList();
        ids.ForEach(id => Points.Remove(id));
        return Task.FromResult(ids.Count > 0);
    }
}
