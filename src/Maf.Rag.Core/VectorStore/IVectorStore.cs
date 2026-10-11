namespace Maf.Rag.Core.VectorStore;

public interface IVectorStore
{
    Task EnsureCollectionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes all chunks of a document, replacing any previously stored version of it.
    /// </summary>
    Task ReplaceDocumentAsync(string documentId, IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken);

    Task<IReadOnlyList<SearchHit>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        double minScore,
        string? source,
        string? documentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentSummary>> ListDocumentsAsync(int limit, CancellationToken cancellationToken);

    Task<bool> DeleteDocumentAsync(string documentId, CancellationToken cancellationToken);
}
