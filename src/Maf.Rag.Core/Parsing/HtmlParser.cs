using System.Net;
using System.Text.RegularExpressions;

namespace Maf.Rag.Core.Parsing;

/// <summary>Lightweight HTML-to-text conversion: drops scripts/styles, keeps block structure.</summary>
public sealed partial class HtmlParser : IDocumentParser
{
    public IReadOnlySet<string> SupportedExtensions { get; } =
        new HashSet<string>([".html", ".htm"], StringComparer.OrdinalIgnoreCase);

    public async Task<string> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(content, leaveOpen: true);
        var html = await reader.ReadToEndAsync(cancellationToken);
        return ToText(html);
    }

    internal static string ToText(string html)
    {
        var text = ScriptOrStyle().Replace(html, " ");
        text = BlockBoundary().Replace(text, "\n\n");
        text = Tag().Replace(text, " ");
        return WebUtility.HtmlDecode(text);
    }

    [GeneratedRegex(@"<(script|style|noscript)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"</?(p|div|br|li|h[1-6]|tr|section|article|header|footer)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBoundary();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();
}
