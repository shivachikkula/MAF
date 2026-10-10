using Maf.Rag.Core;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Maf.Rag.Api.Endpoints;

public static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/search", SearchAsync)
            .WithTags("Search")
            .WithSummary("Semantic search over the knowledge base.");
        return api;
    }

    private static async Task<Ok<SearchApiResponse>> SearchAsync(
        SearchApiRequest request,
        RetrievalService retrieval,
        CancellationToken cancellationToken)
    {
        var hits = await retrieval.SearchAsync(
            new SearchRequest(request.Query, request.TopK, request.MinScore, request.Source, request.DocumentId),
            cancellationToken);
        return TypedResults.Ok(new SearchApiResponse(request.Query, hits));
    }
}
