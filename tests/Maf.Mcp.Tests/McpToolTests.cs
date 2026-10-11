using Maf.Mcp.Server.Clients;
using Maf.Mcp.Server.Tools;

namespace Maf.Mcp.Tests;

public sealed class McpToolTests
{
    [Fact]
    public void Search_results_are_numbered_with_sources()
    {
        var text = KnowledgeBaseTools.FormatSearchResults(new RagSearchResponse("q",
        [
            new RagSearchHit("d1", "handbook.md", "Handbook", 3, " Vacation is 25 days. ", 0.91, null),
            new RagSearchHit("d2", "faq.md", "FAQ", 0, "Ask HR.", 0.5, null),
        ]));

        Assert.StartsWith("[1] Handbook — source: handbook.md (chunk 3, score 0.910)", text, StringComparison.Ordinal);
        Assert.Contains("Vacation is 25 days.", text, StringComparison.Ordinal);
        Assert.Contains("[2] FAQ — source: faq.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_results_explain_nothing_was_found()
    {
        Assert.Contains("No relevant passages", KnowledgeBaseTools.FormatSearchResults(new RagSearchResponse("q", [])), StringComparison.Ordinal);
    }
}
