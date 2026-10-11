namespace Maf.Rag.Core.Parsing;

public sealed class PlainTextParser : IDocumentParser
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>([".txt", ".md", ".markdown", ".csv", ".json", ".log", ".rst"], StringComparer.OrdinalIgnoreCase);

    public async Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
