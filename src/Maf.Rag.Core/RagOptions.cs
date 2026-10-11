using System.ComponentModel.DataAnnotations;

namespace Maf.Rag.Core;

public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Qdrant collection that holds the knowledge-base chunks.</summary>
    [Required]
    public string CollectionName { get; set; } = "knowledge_base";

    /// <summary>Must match the embedding model's output size (text-embedding-3-small = 1536).</summary>
    [Range(8, 8192)]
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>Target chunk size in characters (~4 chars per token).</summary>
    [Range(100, 20_000)]
    public int ChunkSize { get; set; } = 1200;

    [Range(0, 5_000)]
    public int ChunkOverlap { get; set; } = 200;

    /// <summary>Number of chunks sent to the embedding API per request.</summary>
    [Range(1, 2048)]
    public int EmbeddingBatchSize { get; set; } = 64;

    [Range(1, 100)]
    public int DefaultTopK { get; set; } = 5;

    [Range(0.0, 1.0)]
    public double DefaultMinScore { get; set; } = 0.25;
}

public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Optional override for OpenAI-compatible endpoints.</summary>
    public Uri? Endpoint { get; set; }

    [Required]
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
}

public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    [Required]
    public string Host { get; set; } = "localhost";

    /// <summary>gRPC port (Qdrant exposes REST on 6333 and gRPC on 6334).</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 6334;

    public bool UseTls { get; set; }

    public string? ApiKey { get; set; }
}
