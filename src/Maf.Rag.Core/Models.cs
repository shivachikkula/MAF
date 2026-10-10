namespace Maf.Rag.Core;

/// <summary>A document submitted for ingestion, already converted to plain text.</summary>
public sealed record SourceDocument(
    string Source,
    string Content,
    string? Title = null,
    string? DocumentId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>A single embedded unit stored in the vector database.</summary>
public sealed record DocumentChunk(
    Guid Id,
    string DocumentId,
    string Source,
    string Title,
    int ChunkIndex,
    int TotalChunks,
    string Text,
    ReadOnlyMemory<float> Embedding,
    DateTimeOffset IngestedAt,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record IngestionResult(string DocumentId, string Source, string Title, int ChunkCount);

public sealed record SearchRequest(
    string Query,
    int? TopK = null,
    double? MinScore = null,
    string? Source = null,
    string? DocumentId = null);

public sealed record SearchHit(
    string DocumentId,
    string Source,
    string Title,
    int ChunkIndex,
    string Text,
    double Score,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record DocumentSummary(
    string DocumentId,
    string Source,
    string Title,
    int ChunkCount,
    DateTimeOffset IngestedAt);
