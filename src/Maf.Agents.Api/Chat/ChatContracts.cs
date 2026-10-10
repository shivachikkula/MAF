using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Maf.Agents.Api.Chat;

public sealed record ChatHistoryMessage(
    [property: Required, AllowedValues("user", "assistant")] string Role,
    [property: Required, StringLength(32_000)] string Content);

public sealed record ChatRequest(
    [property: Required, StringLength(8_000, MinimumLength = 1)] string Message,
    [property: MaxLength(100)] IReadOnlyList<ChatHistoryMessage>? History = null);

public sealed record ChatResponse(string Answer, IReadOnlyList<AgentStep> Steps);

public sealed record AgentStep(string Agent, string Output, IReadOnlyList<string> ToolCalls);

/// <summary>Event streamed to the client over Server-Sent Events. The SSE event name equals <see cref="Type"/>.</summary>
public sealed record ChatStreamEvent(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Agent = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tool = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Arguments = null)
{
    public static class Types
    {
        public const string AgentStarted = "agent_started";
        public const string Delta = "delta";
        public const string ToolCall = "tool_call";
        public const string ToolResult = "tool_result";
        public const string Final = "final";
        public const string Error = "error";
    }
}
