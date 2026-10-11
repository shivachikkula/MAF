using System.ClientModel;
using Maf.Rag.Core.Chunking;
using Maf.Rag.Core.Parsing;
using Maf.Rag.Core.VectorStore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using Qdrant.Client;

namespace Maf.Rag.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRagCore(this IServiceCollection services)
    {
        services.AddOptions<RagOptions>().BindConfiguration(RagOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<OpenAIOptions>().BindConfiguration(OpenAIOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<QdrantOptions>().BindConfiguration(QdrantOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<IDocumentParser, PlainTextParser>();
        services.AddSingleton<IDocumentParser, HtmlParser>();
        services.AddSingleton<IDocumentParser, PdfParser>();
        services.AddSingleton<DocumentParserResolver>();
        services.AddSingleton<ITextChunker, RecursiveTextChunker>();

        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantClient(o.Host, o.Port, o.UseTls, o.ApiKey, loggerFactory: sp.GetRequiredService<ILoggerFactory>());
        });
        services.AddSingleton<IVectorStore, QdrantVectorStore>();

        services.AddEmbeddingGenerator(sp =>
            {
                var o = sp.GetRequiredService<IOptions<OpenAIOptions>>().Value;
                var rag = sp.GetRequiredService<IOptions<RagOptions>>().Value;
                var clientOptions = new OpenAIClientOptions();
                if (o.Endpoint is not null)
                {
                    clientOptions.Endpoint = o.Endpoint;
                }

                return new OpenAIClient(new ApiKeyCredential(o.ApiKey), clientOptions)
                    .GetEmbeddingClient(o.EmbeddingModel)
                    .AsIEmbeddingGenerator(rag.EmbeddingDimensions);
            })
            .UseOpenTelemetry(sourceName: RagTelemetry.Name)
            .UseLogging();

        services.AddSingleton<IngestionService>();
        services.AddSingleton<RetrievalService>();
        services.AddHostedService<VectorStoreInitializer>();

        return services;
    }
}
