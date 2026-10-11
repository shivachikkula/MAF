using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Maf.Rag.Core.Chunking;

/// <summary>
/// Splits text on the most meaningful boundary available (paragraph, line, sentence, word),
/// recursing to finer separators only for pieces that are still too large, then merges
/// neighbouring pieces into chunks of up to <see cref="RagOptions.ChunkSize"/> characters
/// with <see cref="RagOptions.ChunkOverlap"/> characters of trailing context carried over.
/// </summary>
public sealed partial class RecursiveTextChunker : ITextChunker
{
    private static readonly string[] Separators = ["\n\n", "\n", ". ", "? ", "! ", "; ", ", ", " "];

    private readonly int _chunkSize;
    private readonly int _overlap;

    public RecursiveTextChunker(IOptions<RagOptions> options)
        : this(options.Value.ChunkSize, options.Value.ChunkOverlap)
    {
    }

    public RecursiveTextChunker(int chunkSize, int overlap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(overlap, chunkSize);
        _chunkSize = chunkSize;
        _overlap = overlap;
    }

    public IReadOnlyList<string> Chunk(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return [];
        }

        var chunks = new List<string>();
        foreach (var chunk in Split(normalized, 0))
        {
            var trimmed = chunk.Trim();
            if (trimmed.Length > 0)
            {
                chunks.Add(trimmed);
            }
        }

        return chunks;
    }

    private List<string> Split(string text, int separatorIndex)
    {
        if (text.Length <= _chunkSize)
        {
            return [text];
        }

        // Find the coarsest separator present in this text.
        while (separatorIndex < Separators.Length && !text.Contains(Separators[separatorIndex], StringComparison.Ordinal))
        {
            separatorIndex++;
        }

        if (separatorIndex >= Separators.Length)
        {
            return HardSplit(text);
        }

        var separator = Separators[separatorIndex];
        var pieces = SplitKeepingSeparator(text, separator);

        var result = new List<string>();
        var pending = new List<string>();
        foreach (var piece in pieces)
        {
            if (piece.Length <= _chunkSize)
            {
                pending.Add(piece);
                continue;
            }

            result.AddRange(Merge(pending));
            pending.Clear();
            result.AddRange(Split(piece, separatorIndex + 1));
        }

        result.AddRange(Merge(pending));
        return result;
    }

    /// <summary>Greedily packs small pieces into chunks, carrying an overlap window between chunks.</summary>
    private List<string> Merge(List<string> pieces)
    {
        var chunks = new List<string>();
        var window = new LinkedList<string>();
        var windowLength = 0;

        foreach (var piece in pieces)
        {
            if (windowLength + piece.Length > _chunkSize && window.Count > 0)
            {
                chunks.Add(string.Concat(window));

                // Drop pieces from the front until only the overlap remains (and the next piece fits).
                while (window.Count > 0 && (windowLength > _overlap || windowLength + piece.Length > _chunkSize))
                {
                    windowLength -= window.First!.Value.Length;
                    window.RemoveFirst();
                }
            }

            window.AddLast(piece);
            windowLength += piece.Length;
        }

        if (window.Count > 0)
        {
            chunks.Add(string.Concat(window));
        }

        return chunks;
    }

    private List<string> HardSplit(string text)
    {
        var step = _chunkSize - _overlap;
        var chunks = new List<string>();
        for (var start = 0; start < text.Length; start += step)
        {
            chunks.Add(text.Substring(start, Math.Min(_chunkSize, text.Length - start)));
            if (start + _chunkSize >= text.Length)
            {
                break;
            }
        }

        return chunks;
    }

    private static List<string> SplitKeepingSeparator(string text, string separator)
    {
        var parts = new List<string>();
        var start = 0;
        int index;
        while ((index = text.IndexOf(separator, start, StringComparison.Ordinal)) >= 0)
        {
            var end = index + separator.Length;
            parts.Add(text[start..end]);
            start = end;
        }

        if (start < text.Length)
        {
            parts.Add(text[start..]);
        }

        return parts;
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        var collapsed = ExcessBlankLines().Replace(sb.ToString(), "\n\n");
        return HorizontalWhitespace().Replace(collapsed, " ").Trim();
    }

    [GeneratedRegex(@"\n\s*\n\s*\n+")]
    private static partial Regex ExcessBlankLines();

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex HorizontalWhitespace();
}
