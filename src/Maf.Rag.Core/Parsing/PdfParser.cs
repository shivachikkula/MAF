using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Maf.Rag.Core.Parsing;

public sealed class PdfParser : IDocumentParser
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>([".pdf"], StringComparer.OrdinalIgnoreCase);

    public async Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        // PdfPig needs a seekable stream.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        using var document = PdfDocument.Open(buffer);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            sb.Append(ContentOrderTextExtractor.GetText(page)).Append("\n\n");
        }

        return sb.ToString();
    }
}
