using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Maf.Agents.Api.Chat;

public static class ChatEndpoints
{
    public const string RateLimitPolicy = "chat";

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var chat = app.MapGroup("/api/chat").WithTags("Chat").RequireRateLimiting(RateLimitPolicy);

        chat.MapPost("/stream", Stream)
            .WithSummary("Run the multi-agent workflow and stream progress as Server-Sent Events.");

        chat.MapPost("/", RunAsync)
            .WithSummary("Run the multi-agent workflow and return the final answer.");

        return app;
    }

    private static ServerSentEventsResult<ChatStreamEvent> Stream(
        ChatRequest request,
        ChatOrchestrator orchestrator,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(ChatEndpoints));
        return TypedResults.ServerSentEvents(ToSse(orchestrator.StreamAsync(request, cancellationToken), logger, cancellationToken));
    }

    private static async Task<Results<Ok<ChatResponse>, ProblemHttpResult>> RunAsync(
        ChatRequest request,
        ChatOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await orchestrator.RunAsync(request, cancellationToken));
        }
        catch (AgentWorkflowException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Once the SSE response has started, exceptions can no longer become HTTP error codes,
    /// so failures are converted into a terminal <c>error</c> event instead.
    /// </summary>
    private static async IAsyncEnumerable<SseItem<ChatStreamEvent>> ToSse(
        IAsyncEnumerable<ChatStreamEvent> events,
        ILogger logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = events.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                ChatStreamEvent current;
                ChatStreamEvent? failure = null;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        yield break;
                    }

                    current = enumerator.Current;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    yield break; // client disconnected
                }
                catch (OperationCanceledException)
                {
                    current = default!;
                    failure = new ChatStreamEvent(ChatStreamEvent.Types.Error, Text: "The request timed out.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Chat stream failed");
                    current = default!;
                    failure = new ChatStreamEvent(ChatStreamEvent.Types.Error, Text: "The agents failed to complete the request.");
                }

                if (failure is not null)
                {
                    yield return new SseItem<ChatStreamEvent>(failure, failure.Type);
                    yield break;
                }

                yield return new SseItem<ChatStreamEvent>(current, current.Type);
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }
}
