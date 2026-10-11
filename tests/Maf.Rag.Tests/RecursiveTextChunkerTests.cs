using Maf.Rag.Core.Chunking;

namespace Maf.Rag.Tests;

public sealed class RecursiveTextChunkerTests
{
    [Fact]
    public void Short_text_is_a_single_chunk()
    {
        var chunker = new RecursiveTextChunker(100, 10);
        Assert.Equal(["Hello world."], chunker.Chunk("  Hello world.  "));
    }

    [Fact]
    public void Empty_text_yields_no_chunks()
    {
        Assert.Empty(new RecursiveTextChunker(100, 10).Chunk(" \n\n "));
    }

    [Fact]
    public void Prefers_paragraph_boundaries()
    {
        var para1 = new string('a', 60);
        var para2 = new string('b', 60);
        var chunks = new RecursiveTextChunker(100, 0).Chunk($"{para1}\n\n{para2}");

        Assert.Equal([para1, para2], chunks);
    }

    [Fact]
    public void All_chunks_respect_the_size_limit_and_cover_the_text()
    {
        var sentences = Enumerable.Range(0, 200).Select(i => $"Sentence number {i} talks about topic {i % 7}.");
        var text = string.Join(" ", sentences);
        var chunker = new RecursiveTextChunker(250, 50);

        var chunks = chunker.Chunk(text);

        Assert.True(chunks.Count > 10);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 250));
        foreach (var i in new[] { 0, 57, 123, 199 })
        {
            Assert.Contains(chunks, c => c.Contains($"Sentence number {i} ", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Consecutive_chunks_overlap()
    {
        var text = string.Join(" ", Enumerable.Range(0, 100).Select(i => $"w{i}"));
        var chunks = new RecursiveTextChunker(60, 20).Chunk(text);

        for (var i = 1; i < chunks.Count; i++)
        {
            var lastWordOfPrevious = chunks[i - 1].Split(' ')[^1];
            Assert.Contains(lastWordOfPrevious, chunks[i].Split(' '));
        }
    }

    [Fact]
    public void Text_without_separators_is_hard_split()
    {
        var chunks = new RecursiveTextChunker(100, 10).Chunk(new string('x', 250));
        Assert.All(chunks, c => Assert.True(c.Length <= 100));
        Assert.Equal(3, chunks.Count);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(100, -1)]
    public void Rejects_invalid_configuration(int size, int overlap)
    {
        Assert.ThrowsAny<ArgumentException>(() => new RecursiveTextChunker(size, overlap));
    }
}
