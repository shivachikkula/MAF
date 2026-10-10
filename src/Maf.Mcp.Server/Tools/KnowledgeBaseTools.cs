using System.ComponentModel;
using System.Globalization;
using System.Text;
using Maf.Mcp.Server.Clients;
using ModelContextProtocol.Server;

namespace Maf.Mcp.Server.Tools;

/// <summary>MCP tools that expose the RAG knowledge base to any MCP-capable agent.</summary>
[McpServerToolType]
public sealed class KnowledgeBaseTools(RagApiClient rag, ILogger<KnowledgeBaseTools> logger)
{
    [McpServerTool(Name = "search_knowledge_base", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Semantic search over the organisation's knowledge base. Returns the most relevant passages with " +
                 "their source and relevance score. Use specific, self-contained queries; call multiple times " +
                 "with different phrasings to cover different aspects of a question.")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("Natural-language search query.")] string query,
        [Description("Maximum number of passages to return (1-20).")] int topK = 5,
        [Description("Optional: restrict results to a single source file name, as returned by list_knowledge_base_documents.")] string? source = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("search_knowledge_base: {Query} (topK={TopK}, source={Source})", query, topK, source);
        var response = await rag.SearchAsync(query, Math.Clamp(topK, 1, 20), source, cancellationToken);
        return FormatSearchResults(response);
    }

    [McpServerTool(Name = "list_knowledge_base_documents", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Lists the documents currently indexed in the knowledge base (title, source, size).")]
    public async Task<string> ListDocumentsAsync(CancellationToken cancellationToken = default)
    {
        var documents = await rag.ListDocumentsAsync(cancellationToken);
        if (documents.Count == 0)
        {
            return "The knowledge base is empty.";
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{documents.Count} document(s):");
        foreach (var d in documents)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {d.Title} (source: {d.Source}, chunks: {d.ChunkCount}, ingested: {d.IngestedAt:yyyy-MM-dd})");
        }

        return sb.ToString();
    }

    public static string FormatSearchResults(RagSearchResponse response)
    {
        if (response.Results.Count == 0)
        {
            return $"No relevant passages found for \"{response.Query}\".";
        }

        var sb = new StringBuilder();
        for (var i = 0; i < response.Results.Count; i++)
        {
            var hit = response.Results[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{i + 1}] {hit.Title} — source: {hit.Source} (chunk {hit.ChunkIndex}, score {hit.Score:F3})");
            sb.AppendLine(hit.Text.Trim());
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
