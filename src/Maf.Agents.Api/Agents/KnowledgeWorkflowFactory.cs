using Maf.Agents.Api.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Maf.Agents.Api.Agents;

public interface IKnowledgeWorkflowFactory
{
    Task<Workflow> CreateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Builds the sequential Researcher → Writer workflow. Agents are lightweight, so a fresh
/// workflow is created per request; the expensive pieces (chat client, MCP connection) are shared.
/// </summary>
public sealed class KnowledgeWorkflowFactory(
    IChatClient chatClient,
    IMcpToolProvider mcpTools,
    ILoggerFactory loggerFactory,
    IServiceProvider services) : IKnowledgeWorkflowFactory
{
    public async Task<Workflow> CreateAsync(CancellationToken cancellationToken)
    {
        var tools = await mcpTools.GetToolsAsync(cancellationToken);

        var researcher = new ChatClientAgent(
            chatClient,
            new ChatClientAgentOptions
            {
                Name = AgentDefinitions.ResearcherName,
                Description = AgentDefinitions.ResearcherDescription,
                ChatOptions = new ChatOptions
                {
                    Instructions = AgentDefinitions.ResearcherInstructions,
                    Tools = [.. tools],
                },
            },
            loggerFactory,
            services);

        var writer = new ChatClientAgent(
            new ToolContentStrippingChatClient(chatClient),
            new ChatClientAgentOptions
            {
                Name = AgentDefinitions.WriterName,
                Description = AgentDefinitions.WriterDescription,
                ChatOptions = new ChatOptions { Instructions = AgentDefinitions.WriterInstructions },
            },
            loggerFactory,
            services);

        return AgentWorkflowBuilder.BuildSequential("knowledge-workflow", [researcher, writer]);
    }
}
