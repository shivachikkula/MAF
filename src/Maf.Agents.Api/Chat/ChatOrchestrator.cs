using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Maf.Agents.Api.Agents;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Maf.Agents.Api.Chat;

/// <summary>Runs the multi-agent workflow for one user turn and translates workflow events into client events.</summary>
public sealed class ChatOrchestrator(
    IKnowledgeWorkflowFactory workflowFactory,
    IOptions<AgentsOptions> options,
    ILogger<ChatOrchestrator> logger)
{
    public static readonly ActivitySource ActivitySource = new("Maf.Agents");

    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("agents.chat");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds));
        var ct = timeout.Token;

        var workflow = await workflowFactory.CreateAsync(ct);
        var messages = BuildMessages(request);

        await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages, cancellationToken: ct);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? currentAgent = null;
        var finalAnswer = new StringBuilder();

        await foreach (var evt in run.WatchStreamAsync(ct))
        {
            switch (evt)
            {
                case AgentResponseUpdateEvent update:
                    var agent = ResolveAgentName(update);
                    if (agent != currentAgent)
                    {
                        currentAgent = agent;
                        finalAnswer.Clear();
                        yield return new ChatStreamEvent(ChatStreamEvent.Types.AgentStarted, agent);
                    }

                    foreach (var content in update.Update.Contents)
                    {
                        switch (content)
                        {
                            case TextContent { Text.Length: > 0 } text:
                                finalAnswer.Append(text.Text);
                                yield return new ChatStreamEvent(ChatStreamEvent.Types.Delta, agent, text.Text);
                                break;
                            case FunctionCallContent call:
                                yield return new ChatStreamEvent(
                                    ChatStreamEvent.Types.ToolCall, agent, Tool: call.Name,
                                    Arguments: call.Arguments is null ? null : JsonSerializer.Serialize(call.Arguments));
                                break;
                            case FunctionResultContent result:
                                yield return new ChatStreamEvent(ChatStreamEvent.Types.ToolResult, agent, Tool: result.CallId);
                                break;
                        }
                    }

                    break;

                case WorkflowErrorEvent error:
                    logger.LogError(error.Exception, "Workflow failed");
                    yield return new ChatStreamEvent(ChatStreamEvent.Types.Error, Text: "The agents failed to complete the request.");
                    yield break;

                case ExecutorFailedEvent failed:
                    logger.LogError(failed.Data, "Agent {Executor} failed", failed.ExecutorId);
                    yield return new ChatStreamEvent(ChatStreamEvent.Types.Error, Text: "The agents failed to complete the request.");
                    yield break;

                case WorkflowOutputEvent:
                    // Sequential workflows yield their output once the final agent finishes.
                    activity?.SetTag("agents.answer_length", finalAnswer.Length);
                    yield return new ChatStreamEvent(ChatStreamEvent.Types.Final, currentAgent, finalAnswer.ToString());
                    yield break;
            }
        }
    }

    /// <summary>Non-streaming variant: drains the stream and aggregates per-agent output.</summary>
    public async Task<ChatResponse> RunAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var steps = new List<(string Agent, StringBuilder Output, List<string> Tools)>();
        string? answer = null;

        await foreach (var evt in StreamAsync(request, cancellationToken))
        {
            switch (evt.Type)
            {
                case ChatStreamEvent.Types.AgentStarted:
                    steps.Add((evt.Agent!, new StringBuilder(), []));
                    break;
                case ChatStreamEvent.Types.Delta when steps.Count > 0:
                    steps[^1].Output.Append(evt.Text);
                    break;
                case ChatStreamEvent.Types.ToolCall when steps.Count > 0:
                    steps[^1].Tools.Add($"{evt.Tool}({evt.Arguments})");
                    break;
                case ChatStreamEvent.Types.Final:
                    answer = evt.Text;
                    break;
                case ChatStreamEvent.Types.Error:
                    throw new AgentWorkflowException(evt.Text ?? "Agent workflow failed.");
            }
        }

        return new ChatResponse(
            answer ?? throw new AgentWorkflowException("The workflow ended without producing an answer."),
            steps.Select(s => new AgentStep(s.Agent, s.Output.ToString(), s.Tools)).ToList());
    }

    private List<ChatMessage> BuildMessages(ChatRequest request)
    {
        var history = (request.History ?? [])
            .TakeLast(options.Value.MaxHistoryMessages)
            .Select(m => m.Role == "assistant"
                // Prior answers were produced by the Writer. Tagging them keeps them in the assistant role for the
                // Writer; the workflow host re-labels other agents' messages as user input for everyone else.
                ? new ChatMessage(ChatRole.Assistant, m.Content) { AuthorName = AgentDefinitions.WriterName }
                : new ChatMessage(ChatRole.User, m.Content));

        return [.. history, new ChatMessage(ChatRole.User, request.Message)];
    }

    /// <summary>Executor ids are derived from agent names; prefer the explicit author name when present.</summary>
    internal static string ResolveAgentName(AgentResponseUpdateEvent update)
    {
        if (!string.IsNullOrEmpty(update.Update.AuthorName))
        {
            return update.Update.AuthorName;
        }

        var id = update.ExecutorId;
        foreach (var name in new[] { AgentDefinitions.ResearcherName, AgentDefinitions.WriterName })
        {
            if (id.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return id;
    }
}

public sealed class AgentWorkflowException(string message) : Exception(message);
