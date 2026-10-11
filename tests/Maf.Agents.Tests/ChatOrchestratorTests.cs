using Maf.Agents.Api;
using Maf.Agents.Api.Agents;
using Maf.Agents.Api.Chat;
using Maf.Agents.Api.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Maf.Agents.Tests;

public sealed class ChatOrchestratorTests
{
    internal sealed class FakeTools : IMcpToolProvider
    {
        public int Invocations { get; private set; }

        public Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken)
        {
            AITool search = AIFunctionFactory.Create(
                (string query) =>
                {
                    Invocations++;
                    return $"Full-time employees receive 25 vacation days (query: {query}).";
                },
                "search_knowledge_base",
                "Search the knowledge base.");
            return Task.FromResult<IReadOnlyList<AITool>>([search]);
        }
    }

    private static (ChatOrchestrator Orchestrator, ScriptedChatClient Llm, FakeTools Tools) Create()
    {
        var llm = new ScriptedChatClient();
        var tools = new FakeTools();
        var services = new ServiceCollection().BuildServiceProvider();
        var factory = new KnowledgeWorkflowFactory(llm, tools, NullLoggerFactory.Instance, services);
        var orchestrator = new ChatOrchestrator(factory, Options.Create(new AgentsOptions()), NullLogger<ChatOrchestrator>.Instance);
        return (orchestrator, llm, tools);
    }

    [Fact]
    public async Task Stream_runs_researcher_with_tools_then_writer_and_emits_final_answer()
    {
        var (orchestrator, llm, tools) = Create();

        var events = new List<ChatStreamEvent>();
        await foreach (var e in orchestrator.StreamAsync(new ChatRequest("How many vacation days do I get?"), CancellationToken.None))
        {
            events.Add(e);
        }

        Assert.Equal(1, tools.Invocations);

        var started = events.Where(e => e.Type == ChatStreamEvent.Types.AgentStarted).Select(e => e.Agent).ToList();
        Assert.Equal([AgentDefinitions.ResearcherName, AgentDefinitions.WriterName], started);

        var toolCall = Assert.Single(events, e => e.Type == ChatStreamEvent.Types.ToolCall);
        Assert.Equal("search_knowledge_base", toolCall.Tool);
        Assert.Equal(AgentDefinitions.ResearcherName, toolCall.Agent);

        var final = events[^1];
        Assert.Equal(ChatStreamEvent.Types.Final, final.Type);
        Assert.Equal("Employees get 25 days (source: handbook.md).", final.Text);

        // The writer must see the researcher's notes as part of its context.
        var writerCall = Assert.Single(llm.Calls, c => c.Agent == AgentDefinitions.WriterName);
        Assert.Contains(writerCall.Messages, m => m.Text.Contains("RESEARCH NOTES", StringComparison.Ordinal));
        Assert.Contains(writerCall.Messages, m => m.Text.Contains("How many vacation days", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_aggregates_steps_and_includes_history()
    {
        var (orchestrator, llm, _) = Create();

        var response = await orchestrator.RunAsync(
            new ChatRequest("And for part-time?", [new ChatHistoryMessage("user", "Vacation days?"), new ChatHistoryMessage("assistant", "25 days.")]),
            CancellationToken.None);

        Assert.Equal("Employees get 25 days (source: handbook.md).", response.Answer);
        Assert.Equal([AgentDefinitions.ResearcherName, AgentDefinitions.WriterName], response.Steps.Select(s => s.Agent));
        Assert.Single(response.Steps[0].ToolCalls);

        var firstResearcherCall = llm.Calls.First(c => c.Agent == AgentDefinitions.ResearcherName);
        Assert.Contains(firstResearcherCall.Messages, m => m.Text == "Vacation days?");
        Assert.Contains(firstResearcherCall.Messages, m => m.Text == "25 days.");

        // The Writer authored earlier answers, so it must see them as its own (assistant) turns.
        var writerCall = llm.Calls.Single(c => c.Agent == AgentDefinitions.WriterName);
        Assert.Contains(writerCall.Messages, m => m.Role == ChatRole.Assistant && m.Text == "25 days.");
        Assert.DoesNotContain(writerCall.Messages, m => m.Role == ChatRole.Tool);
        Assert.DoesNotContain(writerCall.Messages.SelectMany(m => m.Contents), c => c is FunctionCallContent or FunctionResultContent);
    }
}
