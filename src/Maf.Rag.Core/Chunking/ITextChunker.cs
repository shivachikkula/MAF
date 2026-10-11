namespace Maf.Rag.Core.Chunking;

public interface ITextChunker
{
    IReadOnlyList<string> Chunk(string text);
}
