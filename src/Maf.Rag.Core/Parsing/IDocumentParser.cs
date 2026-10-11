namespace Maf.Rag.Core.Parsing;

/// <summary>Converts an uploaded file into plain text suitable for chunking.</summary>
public interface IDocumentParser
{
    IReadOnlySet<string> SupportedExtensions { get; }

    Task<string> ParseAsync(Stream content, CancellationToken cancellationToken);
}

public sealed class UnsupportedDocumentException(string fileName)
    : Exception($"Unsupported document type: '{Path.GetExtension(fileName)}'.");

public sealed class DocumentParserResolver(IEnumerable<IDocumentParser> parsers)
{
    private readonly Dictionary<string, IDocumentParser> _byExtension = parsers
        .SelectMany(p => p.SupportedExtensions.Select(ext => (ext, p)))
        .ToDictionary(x => x.ext, x => x.p, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> SupportedExtensions => _byExtension.Keys;

    public IDocumentParser Resolve(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return _byExtension.TryGetValue(extension, out var parser)
            ? parser
            : throw new UnsupportedDocumentException(fileName);
    }
}
