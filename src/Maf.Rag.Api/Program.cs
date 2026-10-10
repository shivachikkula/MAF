using System.Threading.RateLimiting;
using Maf.Rag.Api;
using Maf.Rag.Api.Endpoints;
using Maf.Rag.Core;
using Maf.Rag.Core.VectorStore;
using Maf.ServiceDefaults;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddRagCore();
builder.Services.AddHealthChecks().AddCheck<QdrantHealthCheck>("qdrant");

var maxUploadBytes = builder.Configuration.GetValue<long?>("Rag:MaxUploadBytes") ?? 25 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxUploadBytes);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUploadBytes);

builder.Services.AddValidation();
builder.Services.AddExceptionHandler<RagExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Ingestion, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGroup("/api")
    .RequireApiKey()
    .MapDocumentEndpoints()
    .MapSearchEndpoints();

app.MapDefaultEndpoints();

await app.RunAsync();

public partial class Program;
