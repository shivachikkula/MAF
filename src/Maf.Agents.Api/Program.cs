using System.ClientModel;
using System.Threading.RateLimiting;
using Maf.Agents.Api;
using Maf.Agents.Api.Agents;
using Maf.Agents.Api.Chat;
using Maf.Agents.Api.Mcp;
using Maf.ServiceDefaults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOptions<OpenAIChatOptions>().BindConfiguration(OpenAIChatOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<McpOptions>().BindConfiguration(McpOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AgentsOptions>().BindConfiguration(AgentsOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);

// One shared chat client: OpenTelemetry (gen_ai.* spans/metrics) + logging wrap the OpenAI client.
builder.Services.AddChatClient(sp =>
    {
        var o = sp.GetRequiredService<IOptions<OpenAIChatOptions>>().Value;
        var clientOptions = new OpenAIClientOptions();
        if (o.Endpoint is not null)
        {
            clientOptions.Endpoint = o.Endpoint;
        }

        return new OpenAIClient(new ApiKeyCredential(o.ApiKey), clientOptions).GetChatClient(o.ChatModel).AsIChatClient();
    })
    .UseOpenTelemetry(sourceName: "Maf.Agents", configure: c => c.EnableSensitiveData = builder.Environment.IsDevelopment())
    .UseLogging();

// The MCP transport manages its own long-lived streams, so it bypasses the standard per-attempt timeout.
builder.Services.AddHttpClient(McpToolProvider.HttpClientName)
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler(o =>
    {
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
    });

builder.Services.AddSingleton<IMcpToolProvider, McpToolProvider>();
builder.Services.AddSingleton<IKnowledgeWorkflowFactory, KnowledgeWorkflowFactory>();
builder.Services.AddSingleton<ChatOrchestrator>();
builder.Services.AddHealthChecks().AddCheck<McpHealthCheck>("mcp");

builder.Services.AddValidation();
builder.Services.AddOpenApi();

var agentsOptions = builder.Configuration.GetSection(AgentsOptions.SectionName).Get<AgentsOptions>() ?? new AgentsOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Per-client fixed window to stop abuse, plus a global concurrency cap to protect LLM quota.
    options.AddPolicy(ChatEndpoints.RateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        ctx.Request.Path.StartsWithSegments("/api/chat")
            ? RateLimitPartition.GetConcurrencyLimiter("chat-global", _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = agentsOptions.MaxConcurrentRuns,
                QueueLimit = agentsOptions.MaxConcurrentRuns,
            })
            : RateLimitPartition.GetNoLimiter("other"));
});

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .WithMethods("GET", "POST")
    .WithHeaders("Content-Type")));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapChatEndpoints();
app.MapDefaultEndpoints();

await app.RunAsync();

public partial class Program;
