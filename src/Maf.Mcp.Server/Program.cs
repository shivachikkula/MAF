using Maf.Mcp.Server.Clients;
using Maf.Mcp.Server.Tools;
using Maf.ServiceDefaults;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<RagApiOptions>()
    .BindConfiguration(RagApiOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<RagApiClient>((sp, http) =>
{
    var options = sp.GetRequiredService<IOptions<RagApiOptions>>().Value;
    http.BaseAddress = options.BaseUrl;
    if (!string.IsNullOrEmpty(options.ApiKey))
    {
        http.DefaultRequestHeaders.Add(ApiKeyOptions.HeaderName, options.ApiKey);
    }
});

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "maf-knowledge-base", Version = "1.0.0" })
    // Stateless mode: no server-side session affinity, so replicas can sit behind any load balancer.
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<KnowledgeBaseTools>();

var app = builder.Build();

app.UseApiKeyFor("/mcp");
app.MapMcp("/mcp");
app.MapDefaultEndpoints();

await app.RunAsync();
