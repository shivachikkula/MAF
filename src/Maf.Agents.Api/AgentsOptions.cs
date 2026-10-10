using System.ComponentModel.DataAnnotations;

namespace Maf.Agents.Api;

public sealed class OpenAIChatOptions
{
    public const string SectionName = "OpenAI";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Optional override for OpenAI-compatible endpoints.</summary>
    public Uri? Endpoint { get; set; }

    [Required]
    public string ChatModel { get; set; } = "gpt-5-mini";
}

public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    [Required]
    public Uri Endpoint { get; set; } = new("http://localhost:5102/mcp");

    public string? ApiKey { get; set; }

    [Range(0, 1440)]
    public int ToolCacheMinutes { get; set; } = 5;
}

public sealed class AgentsOptions
{
    public const string SectionName = "Agents";

    [Range(5, 3600)]
    public int RequestTimeoutSeconds { get; set; } = 180;

    [Range(1, 10_000)]
    public int MaxConcurrentRuns { get; set; } = 20;

    [Range(0, 200)]
    public int MaxHistoryMessages { get; set; } = 20;
}
