using Microsoft.Extensions.AI;

namespace Maf.Agents.Api.Agents;

/// <summary>
/// In a sequential workflow the next agent receives the previous agent's full transcript, including its
/// tool calls and raw tool results. Agents without tools don't need (and the API may reject) those messages,
/// and the raw results duplicate what the previous agent already summarised, so they are removed here.
/// </summary>
public sealed class ToolContentStrippingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => base.GetResponseAsync(Strip(messages), options, cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(Strip(messages), options, cancellationToken);

    internal static List<ChatMessage> Strip(IEnumerable<ChatMessage> messages)
    {
        var result = new List<ChatMessage>();
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.Tool)
            {
                continue;
            }

            if (!message.Contents.Any(c => c is FunctionCallContent or FunctionResultContent))
            {
                result.Add(message);
                continue;
            }

            var kept = message.Contents.Where(c => c is not (FunctionCallContent or FunctionResultContent)).ToList();
            if (kept.Count > 0)
            {
                var clone = message.Clone();
                clone.Contents = kept;
                result.Add(clone);
            }
        }

        return result;
    }
}
