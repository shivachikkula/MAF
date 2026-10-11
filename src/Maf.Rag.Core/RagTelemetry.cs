using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Maf.Rag.Core;

internal static class RagTelemetry
{
    public const string Name = "Maf.Rag";

    public static readonly ActivitySource ActivitySource = new(Name);

    private static readonly Meter Meter = new(Name);

    public static readonly Counter<long> DocumentsIngested =
        Meter.CreateCounter<long>("maf.rag.documents_ingested", description: "Documents ingested into the vector store.");

    public static readonly Counter<long> ChunksIngested =
        Meter.CreateCounter<long>("maf.rag.chunks_ingested", description: "Chunks embedded and stored.");

    public static readonly Histogram<int> SearchHits =
        Meter.CreateHistogram<int>("maf.rag.search_hits", description: "Number of chunks returned per search.");
}
