using System.Text.Json;
using Maf.Rag.Core;
using Maf.Rag.Core.VectorStore;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Maf.Rag.Api.Endpoints;

public static class DocumentEndpoints
{
    public static RouteGroupBuilder MapDocumentEndpoints(this RouteGroupBuilder api)
    {
        var docs = api.MapGroup("/documents").WithTags("Documents");

        docs.MapPost("/", UploadAsync)
            .DisableAntiforgery()
            .RequireRateLimiting(RateLimitPolicies.Ingestion)
            .WithSummary("Upload a file (txt, md, html, pdf, ...) and index it into the knowledge base.");

        docs.MapPost("/text", IngestTextAsync)
            .RequireRateLimiting(RateLimitPolicies.Ingestion)
            .WithSummary("Index raw text content into the knowledge base.");

        docs.MapGet("/", ListAsync).WithSummary("List indexed documents.");

        docs.MapDelete("/{documentId}", DeleteAsync).WithSummary("Remove a document and all of its chunks.");

        return api;
    }

    private static async Task<Results<Created<IngestionResult>, ProblemHttpResult>> UploadAsync(
        IFormFile file,
        [FromForm] string? title,
        [FromForm] string? metadata,
        IngestionService ingestion,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return TypedResults.Problem("The uploaded file is empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        Dictionary<string, string>? meta = null;
        if (!string.IsNullOrWhiteSpace(metadata))
        {
            try
            {
                meta = JsonSerializer.Deserialize<Dictionary<string, string>>(metadata);
            }
            catch (JsonException)
            {
                return TypedResults.Problem("'metadata' must be a JSON object of string values.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var fileName = Path.GetFileName(file.FileName);
        await using var stream = file.OpenReadStream();
        var result = await ingestion.IngestFileAsync(stream, fileName, title, meta, cancellationToken);
        return TypedResults.Created($"/api/documents/{result.DocumentId}", result);
    }

    private static async Task<Created<IngestionResult>> IngestTextAsync(
        IngestTextRequest request,
        IngestionService ingestion,
        CancellationToken cancellationToken)
    {
        var result = await ingestion.IngestAsync(
            new SourceDocument(request.Source, request.Content, request.Title, request.DocumentId, request.Metadata),
            cancellationToken);
        return TypedResults.Created($"/api/documents/{result.DocumentId}", result);
    }

    private static async Task<Ok<IReadOnlyList<DocumentSummary>>> ListAsync(
        IVectorStore store,
        CancellationToken cancellationToken,
        int limit = 100)
        => TypedResults.Ok(await store.ListDocumentsAsync(Math.Clamp(limit, 1, 1000), cancellationToken));

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        string documentId,
        IVectorStore store,
        CancellationToken cancellationToken)
        => await store.DeleteDocumentAsync(documentId, cancellationToken) ? TypedResults.NoContent() : TypedResults.NotFound();
}
