using System.Net;
using System.Net.Http.Json;
using Maf.Agents.Api.Mcp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Agents.Tests;

public sealed class ChatEndpointTests : IClassFixture<ChatEndpointTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("OpenAI:ApiKey", "test-key");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient, ScriptedChatClient>();
                services.RemoveAll<IMcpToolProvider>();
                services.AddSingleton<IMcpToolProvider, ChatOrchestratorTests.FakeTools>();
            });
        }
    }

    private readonly HttpClient _client;

    public ChatEndpointTests(Factory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Stream_endpoint_emits_server_sent_events_in_order()
    {
        using var response = await _client.PostAsJsonAsync("/api/chat/stream", new { message = "How many vacation days?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        var eventNames = body.Split('\n')
            .Where(l => l.StartsWith("event: ", StringComparison.Ordinal))
            .Select(l => l["event: ".Length..].Trim())
            .Distinct()
            .ToList();

        Assert.Equal(["agent_started", "tool_call", "tool_result", "delta", "final"], eventNames);
        Assert.Contains("\"type\":\"final\"", body, StringComparison.Ordinal);
        Assert.Contains("Employees get 25 days", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_endpoint_returns_answer_and_steps()
    {
        var result = await _client.PostAsJsonAsync("/api/chat", new { message = "How many vacation days?" });
        result.EnsureSuccessStatusCode();

        var json = await result.Content.ReadFromJsonAsync<Maf.Agents.Api.Chat.ChatResponse>();
        Assert.NotNull(json);
        Assert.Equal("Employees get 25 days (source: handbook.md).", json.Answer);
        Assert.Equal(2, json.Steps.Count);
    }

    [Theory]
    [InlineData("{\"message\":\"\"}")]
    [InlineData("{\"message\":\"hi\",\"history\":[{\"role\":\"system\",\"content\":\"x\"}]}")]
    public async Task Invalid_requests_are_rejected(string payload)
    {
        using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/chat", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Liveness_probe_is_healthy()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
