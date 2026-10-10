using System.ComponentModel.DataAnnotations;

namespace Maf.Rag.Api.Endpoints;

public sealed record IngestTextRequest(
    [property: Required, StringLength(512, MinimumLength = 1)] string Source,
    [property: Required, StringLength(5_000_000, MinimumLength = 1)] string Content,
    [property: StringLength(512)] string? Title = null,
    [property: StringLength(128)] string? DocumentId = null,
    Dictionary<string, string>? Metadata = null);

public sealed record SearchApiRequest(
    [property: Required, StringLength(4_000, MinimumLength = 1)] string Query,
    [property: Range(1, 50)] int? TopK = null,
    [property: Range(0.0, 1.0)] double? MinScore = null,
    [property: StringLength(512)] string? Source = null,
    [property: StringLength(128)] string? DocumentId = null);

public sealed record SearchApiResponse(string Query, IReadOnlyList<Maf.Rag.Core.SearchHit> Results);
