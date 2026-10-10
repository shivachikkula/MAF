using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;

namespace Maf.Mcp.Server.Clients;

public sealed class RagApiOptions
{
    public const string SectionName = "RagApi";

    [Required]
    public Uri BaseUrl { get; set; } = new("http://localhost:5101");

    public string? ApiKey { get; set; }
}

public sealed record RagSearchHit(
    string DocumentId,
    string Source,
    string Title,
    int ChunkIndex,
    string Text,
    double Score,
    Dictionary<string, string>? Metadata);

public sealed record RagSearchResponse(string Query, IReadOnlyList<RagSearchHit> Results);

public sealed record RagDocument(string DocumentId, string Source, string Title, int ChunkCount, DateTimeOffset IngestedAt);

/// <summary>Typed client for the RAG API. Resilience (retries, timeouts, circuit breaker) comes from service defaults.</summary>
public sealed class RagApiClient(HttpClient http)
{
    public async Task<RagSearchResponse> SearchAsync(string query, int topK, string? source, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("api/search", new { query, topK, source }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RagSearchResponse>(cancellationToken)
            ?? new RagSearchResponse(query, []);
    }

    public async Task<IReadOnlyList<RagDocument>> ListDocumentsAsync(CancellationToken cancellationToken)
        => await http.GetFromJsonAsync<List<RagDocument>>("api/documents", cancellationToken) ?? [];
}
