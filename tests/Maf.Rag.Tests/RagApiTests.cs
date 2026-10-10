using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Maf.Rag.Core;
using Maf.Rag.Core.VectorStore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Maf.Rag.Tests;

public sealed class RagApiTests : IClassFixture<RagApiTests.Factory>
{
    public const string ApiKey = "test-secret";

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("OpenAI:ApiKey", "unused");
            builder.UseSetting("Security:ApiKey", ApiKey);
            builder.UseSetting("Rag:DefaultMinScore", "0");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IVectorStore>();
                services.AddSingleton<IVectorStore, InMemoryVectorStore>();
                services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, FakeEmbeddingGenerator>();
            });
        }
    }

    private readonly Factory _factory;

    public RagApiTests(Factory factory) => _factory = factory;

    private HttpClient Client(string? key = ApiKey)
    {
        var client = _factory.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", key);
        }

        return client;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task Requests_without_valid_api_key_are_unauthorized(string? key)
    {
        var response = await Client(key).GetAsync("/api/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_then_search_then_delete()
    {
        var client = Client();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("Remote work is allowed three days per week."u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "remote-work.txt");
        form.Add(new StringContent("Remote Work Policy"), "title");
        form.Add(new StringContent("{\"department\":\"hr\"}"), "metadata");

        var upload = await client.PostAsync("/api/documents", form);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var ingested = await upload.Content.ReadFromJsonAsync<IngestionResult>();
        Assert.Equal("Remote Work Policy", ingested!.Title);

        var search = await client.PostAsJsonAsync("/api/search", new { query = "remote work days" });
        search.EnsureSuccessStatusCode();
        var body = await search.Content.ReadAsStringAsync();
        Assert.Contains("three days per week", body, StringComparison.Ordinal);
        Assert.Contains("\"department\":\"hr\"", body, StringComparison.Ordinal);

        var delete = await client.DeleteAsync($"/api/documents/{ingested.DocumentId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/documents/{ingested.DocumentId}")).StatusCode);
    }

    [Fact]
    public async Task Unsupported_file_type_returns_415()
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2]), "file", "x.exe" } };
        var response = await Client().PostAsync("/api/documents", form);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_search_request_returns_400()
    {
        var response = await Client().PostAsJsonAsync("/api/search", new { query = "", topK = 500 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
