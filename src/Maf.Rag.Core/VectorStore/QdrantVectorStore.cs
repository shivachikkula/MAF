using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace Maf.Rag.Core.VectorStore;

public sealed class QdrantVectorStore(
    QdrantClient client,
    IOptions<RagOptions> options,
    ILogger<QdrantVectorStore> logger) : IVectorStore
{
    private static class Fields
    {
        public const string DocumentId = "document_id";
        public const string Source = "source";
        public const string Title = "title";
        public const string ChunkIndex = "chunk_index";
        public const string TotalChunks = "total_chunks";
        public const string Text = "text";
        public const string IngestedAt = "ingested_at";
        public const string Metadata = "metadata";
    }

    private readonly RagOptions _options = options.Value;
    private string Collection => _options.CollectionName;

    public async Task EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        if (await client.CollectionExistsAsync(Collection, cancellationToken))
        {
            return;
        }

        logger.LogInformation("Creating Qdrant collection {Collection} ({Dimensions} dims, cosine)", Collection, _options.EmbeddingDimensions);
        try
        {
            await client.CreateCollectionAsync(
                Collection,
                new VectorParams { Size = (ulong)_options.EmbeddingDimensions, Distance = Distance.Cosine },
                cancellationToken: cancellationToken);
        }
        catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.AlreadyExists)
        {
            // Another replica won the race; that's fine.
            return;
        }

        await client.CreatePayloadIndexAsync(Collection, Fields.DocumentId, PayloadSchemaType.Keyword, cancellationToken: cancellationToken);
        await client.CreatePayloadIndexAsync(Collection, Fields.Source, PayloadSchemaType.Keyword, cancellationToken: cancellationToken);
        await client.CreatePayloadIndexAsync(Collection, Fields.ChunkIndex, PayloadSchemaType.Integer, cancellationToken: cancellationToken);
    }

    public async Task ReplaceDocumentAsync(string documentId, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        // Point IDs are deterministic per (document, chunk index), so upserting overwrites the previous
        // version in place. Afterwards we drop any leftover chunks beyond the new chunk count. This keeps
        // the document searchable throughout a re-ingest instead of deleting first.
        var points = chunks.Select(ToPoint).ToList();
        foreach (var batch in points.Chunk(256))
        {
            await client.UpsertAsync(Collection, batch, wait: true, cancellationToken: cancellationToken);
        }

        Filter stale = MatchKeyword(Fields.DocumentId, documentId)
            & Range(Fields.ChunkIndex, new Qdrant.Client.Grpc.Range { Gte = chunks.Count });
        await client.DeleteAsync(Collection, stale, wait: true, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        double minScore,
        string? source,
        string? documentId,
        CancellationToken cancellationToken)
    {
        Filter? filter = null;
        if (!string.IsNullOrWhiteSpace(source))
        {
            filter = MatchKeyword(Fields.Source, source);
        }

        if (!string.IsNullOrWhiteSpace(documentId))
        {
            Filter byDoc = MatchKeyword(Fields.DocumentId, documentId);
            filter = filter is null ? byDoc : filter & byDoc;
        }

        var points = await client.QueryAsync(
            Collection,
            query: queryVector.ToArray(),
            filter: filter,
            scoreThreshold: (float)minScore,
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: cancellationToken);

        return points.Select(p => new SearchHit(
            DocumentId: GetString(p.Payload, Fields.DocumentId),
            Source: GetString(p.Payload, Fields.Source),
            Title: GetString(p.Payload, Fields.Title),
            ChunkIndex: (int)GetLong(p.Payload, Fields.ChunkIndex),
            Text: GetString(p.Payload, Fields.Text),
            Score: p.Score,
            Metadata: GetMetadata(p.Payload))).ToList();
    }

    public async Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(int limit, CancellationToken cancellationToken)
    {
        // Every document has exactly one chunk with index 0, which carries the document-level payload.
        Filter firstChunks = Match(Fields.ChunkIndex, 0);
        var documents = new List<DocumentSummary>();
        PointId? offset = null;

        do
        {
            var page = await client.ScrollAsync(
                Collection,
                filter: firstChunks,
                limit: (uint)Math.Min(256, limit - documents.Count),
                offset: offset,
                payloadSelector: new WithPayloadSelector
                {
                    Include = new PayloadIncludeSelector
                    {
                        Fields = { Fields.DocumentId, Fields.Source, Fields.Title, Fields.TotalChunks, Fields.IngestedAt },
                    },
                },
                cancellationToken: cancellationToken);

            documents.AddRange(page.Result.Select(p => new DocumentSummary(
                DocumentId: GetString(p.Payload, Fields.DocumentId),
                Source: GetString(p.Payload, Fields.Source),
                Title: GetString(p.Payload, Fields.Title),
                ChunkCount: (int)GetLong(p.Payload, Fields.TotalChunks),
                IngestedAt: DateTimeOffset.TryParse(GetString(p.Payload, Fields.IngestedAt), out var at) ? at : default)));

            offset = page.NextPageOffset;
        }
        while (offset is not null && documents.Count < limit);

        return documents.OrderByDescending(d => d.IngestedAt).ToList();
    }

    public async Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken)
    {
        Filter byDoc = MatchKeyword(Fields.DocumentId, documentId);
        var count = await client.CountAsync(Collection, byDoc, exact: true, cancellationToken: cancellationToken);
        if (count == 0)
        {
            return false;
        }

        await client.DeleteAsync(Collection, byDoc, wait: true, cancellationToken: cancellationToken);
        return true;
    }

    private static PointStruct ToPoint(DocumentChunk chunk)
    {
        var metadata = new Struct();
        foreach (var (key, value) in chunk.Metadata)
        {
            metadata.Fields[key] = value;
        }

        return new PointStruct
        {
            Id = chunk.Id,
            Vectors = chunk.Embedding.ToArray(),
            Payload =
            {
                [Fields.DocumentId] = chunk.DocumentId,
                [Fields.Source] = chunk.Source,
                [Fields.Title] = chunk.Title,
                [Fields.ChunkIndex] = chunk.ChunkIndex,
                [Fields.TotalChunks] = chunk.TotalChunks,
                [Fields.Text] = chunk.Text,
                [Fields.IngestedAt] = chunk.IngestedAt.ToString("O"),
                [Fields.Metadata] = new Value { StructValue = metadata },
            },
        };
    }

    private static string GetString(MapField<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? v.StringValue : string.Empty;

    private static long GetLong(MapField<string, Value> payload, string key)
        => payload.TryGetValue(key, out var v) ? v.IntegerValue : 0;

    private static Dictionary<string, string> GetMetadata(MapField<string, Value> payload)
        => payload.TryGetValue(Fields.Metadata, out var v) && v.StructValue is { } s
            ? s.Fields.ToDictionary(f => f.Key, f => f.Value.StringValue)
            : [];
}
