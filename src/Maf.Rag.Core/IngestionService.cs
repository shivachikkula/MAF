using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Maf.Rag.Core.Chunking;
using Maf.Rag.Core.Parsing;
using Maf.Rag.Core.VectorStore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Maf.Rag.Core;

/// <summary>Parse → chunk → embed → store. Re-ingesting the same document replaces it (idempotent).</summary>
public sealed class IngestionService(
    DocumentParserResolver parsers,
    ITextChunker chunker,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    IVectorStore vectorStore,
    IOptions<RagOptions> options,
    TimeProvider timeProvider,
    ILogger<IngestionService> logger)
{
    private readonly RagOptions _options = options.Value;

    public async Task<IngestionResult> IngestFileAsync(
        Stream content,
        string fileName,
        string? title,
        IReadOnlyDictionary<string, string>? metadata,
        CancellationToken cancellationToken)
    {
        var parser = parsers.Resolve(fileName);
        var text = await parser.ParseAsync(content, cancellationToken);
        return await IngestAsync(new SourceDocument(fileName, text, title, Metadata: metadata), cancellationToken);
    }

    public async Task<IngestionResult> IngestAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Source);

        using var activity = RagTelemetry.ActivitySource.StartActivity("rag.ingest");
        var documentId = string.IsNullOrWhiteSpace(document.DocumentId) ? DeriveDocumentId(document.Source) : document.DocumentId;
        var title = string.IsNullOrWhiteSpace(document.Title) ? Path.GetFileNameWithoutExtension(document.Source) : document.Title;
        activity?.SetTag("rag.document_id", documentId);

        var texts = chunker.Chunk(document.Content);
        if (texts.Count == 0)
        {
            throw new EmptyDocumentException(document.Source);
        }

        var vectors = new List<ReadOnlyMemory<float>>(texts.Count);
        foreach (var batch in texts.Chunk(_options.EmbeddingBatchSize))
        {
            // Prefix with the title so each chunk carries document-level context into the embedding.
            var inputs = batch.Select(t => $"{title}\n\n{t}");
            var generated = await embeddings.GenerateAsync(inputs, cancellationToken: cancellationToken);
            vectors.AddRange(generated.Select(e => e.Vector));
        }

        var ingestedAt = timeProvider.GetUtcNow();
        var metadata = document.Metadata ?? new Dictionary<string, string>();
        var chunks = texts.Select((text, i) => new DocumentChunk(
            Id: ChunkId(documentId, i),
            DocumentId: documentId,
            Source: document.Source,
            Title: title,
            ChunkIndex: i,
            TotalChunks: texts.Count,
            Text: text,
            Embedding: vectors[i],
            IngestedAt: ingestedAt,
            Metadata: metadata)).ToList();

        await vectorStore.ReplaceDocumentAsync(documentId, chunks, cancellationToken);

        RagTelemetry.DocumentsIngested.Add(1);
        RagTelemetry.ChunksIngested.Add(chunks.Count);
        activity?.SetTag("rag.chunk_count", chunks.Count);
        logger.LogInformation("Ingested {Source} as {DocumentId} ({ChunkCount} chunks)", document.Source, documentId, chunks.Count);

        return new IngestionResult(documentId, document.Source, title, chunks.Count);
    }

    /// <summary>Stable id from the source name, so re-uploading the same file replaces it.</summary>
    internal static string DeriveDocumentId(string source)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.Trim().ToUpperInvariant())))[..32];

    internal static Guid ChunkId(string documentId, int index)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{documentId}:{index}"), hash);
        return new Guid(hash[..16]);
    }
}

public sealed class EmptyDocumentException(string source)
    : Exception($"Document '{source}' contains no extractable text.");
