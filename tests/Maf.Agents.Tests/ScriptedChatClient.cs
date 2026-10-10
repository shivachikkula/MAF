using System.Runtime.CompilerServices;
using Maf.Agents.Api.Agents;
using Microsoft.Extensions.AI;

namespace Maf.Agents.Tests;

/// <summary>
/// Deterministic stand-in for the LLM. The Researcher first requests a tool call and then writes notes
/// from the tool result; the Writer streams a final answer. Every request is recorded for assertions.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    public List<(string Agent, List<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var list = messages.ToList();
        var isResearcher = options?.Instructions?.Contains("You are the Researcher", StringComparison.Ordinal) == true;
        Calls.Add((isResearcher ? AgentDefinitions.ResearcherName : AgentDefinitions.WriterName, list, options));

        if (isResearcher)
        {
            var toolResult = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>().LastOrDefault();
            if (toolResult is null)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant,
                [
                    new FunctionCallContent("call_1", "search_knowledge_base", new Dictionary<string, object?> { ["query"] = "vacation policy" }),
                ]);
                yield break;
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, "RESEARCH NOTES\n");
            yield return new ChatResponseUpdate(ChatRole.Assistant, $"- {toolResult.Result} [source: handbook.md]");
            yield break;
        }

        foreach (var token in new[] { "Employees get ", "25 days ", "(source: handbook.md)." })
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, token);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
