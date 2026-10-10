using Maf.Rag.Core;
using Maf.Rag.Core.Chunking;
using Maf.Rag.Core.Parsing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Rag.Tests;

public sealed class IngestionServiceTests
{
    private readonly InMemoryVectorStore _store = new();
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private readonly IngestionService _ingestion;
    private readonly RetrievalService _retrieval;

    public IngestionServiceTests()
    {
        var options = Options.Create(new RagOptions { ChunkSize = 200, ChunkOverlap = 20, EmbeddingBatchSize = 4, DefaultMinScore = 0 });
        _ingestion = new IngestionService(
            new DocumentParserResolver([new PlainTextParser(), new HtmlParser()]),
            new RecursiveTextChunker(options),
            _embeddings,
            _store,
            options,
            TimeProvider.System,
            NullLogger<IngestionService>.Instance);
        _retrieval = new RetrievalService(_embeddings, _store, options);
    }

    [Fact]
    public async Task Ingest_chunks_embeds_in_batches_and_stores()
    {
        var content = string.Join("\n\n", Enumerable.Range(0, 20).Select(i => $"Paragraph {i}: " + new string('z', 100)));

        var result = await _ingestion.IngestAsync(new SourceDocument("guide.md", content), CancellationToken.None);

        Assert.Equal("guide", result.Title);
        Assert.Equal(result.ChunkCount, _store.Points.Count);
        Assert.Equal((int)Math.Ceiling(result.ChunkCount / 4.0), _embeddings.Calls);
        Assert.All(_store.Points.Values, p => Assert.Equal(result.ChunkCount, p.TotalChunks));
    }

    [Fact]
    public async Task Reingesting_same_source_replaces_previous_version()
    {
        var longText = string.Join("\n\n", Enumerable.Range(0, 10).Select(i => $"Old section {i} " + new string('q', 150)));
        var first = await _ingestion.IngestAsync(new SourceDocument("policy.txt", longText), CancellationToken.None);

        var second = await _ingestion.IngestAsync(new SourceDocument("policy.txt", "Short new version."), CancellationToken.None);

        Assert.Equal(first.DocumentId, second.DocumentId);
        var remaining = Assert.Single(_store.Points.Values);
        Assert.Equal("Short new version.", remaining.Text);
    }

    [Fact]
    public async Task Search_returns_most_similar_chunk_first()
    {
        await _ingestion.IngestAsync(new SourceDocument("a.txt", "apples and bananas are fruit"), CancellationToken.None);
        await _ingestion.IngestAsync(new SourceDocument("b.txt", "xylophone jazz quartet"), CancellationToken.None);

        var hits = await _retrieval.SearchAsync(new SearchRequest("bananas apples fruit", TopK: 2), CancellationToken.None);

        Assert.Equal("a.txt", hits[0].Source);
    }

    [Fact]
    public async Task Search_can_filter_by_source()
    {
        await _ingestion.IngestAsync(new SourceDocument("a.txt", "apples and bananas"), CancellationToken.None);
        await _ingestion.IngestAsync(new SourceDocument("b.txt", "apples and cherries"), CancellationToken.None);

        var hits = await _retrieval.SearchAsync(new SearchRequest("apples", Source: "b.txt"), CancellationToken.None);

        Assert.All(hits, h => Assert.Equal("b.txt", h.Source));
    }

    [Fact]
    public async Task Html_file_is_parsed_to_text()
    {
        await using var stream = new MemoryStream("<html><script>evil()</script><h1>Title</h1><p>Body &amp; soul</p></html>"u8.ToArray());

        await _ingestion.IngestFileAsync(stream, "page.html", null, null, CancellationToken.None);

        var chunk = Assert.Single(_store.Points.Values);
        Assert.Contains("Body & soul", chunk.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("evil", chunk.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_and_empty_documents_are_rejected()
    {
        await using var stream = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<UnsupportedDocumentException>(() =>
            _ingestion.IngestFileAsync(stream, "binary.exe", null, null, CancellationToken.None));
        await Assert.ThrowsAsync<EmptyDocumentException>(() =>
            _ingestion.IngestAsync(new SourceDocument("empty.txt", "   "), CancellationToken.None));
    }

    [Fact]
    public void Chunk_ids_are_deterministic_and_unique()
    {
        Assert.Equal(IngestionService.ChunkId("doc", 1), IngestionService.ChunkId("doc", 1));
        Assert.NotEqual(IngestionService.ChunkId("doc", 1), IngestionService.ChunkId("doc", 2));
        Assert.Equal(IngestionService.DeriveDocumentId("Guide.md"), IngestionService.DeriveDocumentId(" guide.md "));
    }
}
