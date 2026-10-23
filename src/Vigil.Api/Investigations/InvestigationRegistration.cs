using Microsoft.Agents.AI.Workflows.Checkpointing;
using Vigil.Agents;
using Vigil.Api.Cases;
using Vigil.Api.Investigations;
using Vigil.Graph;
using Vigil.Infrastructure.Workflow;

// In the DI namespace, like the framework's own Add* extensions, so Program.cs needs no extra using.
namespace Microsoft.Extensions.DependencyInjection;

public static class InvestigationRegistration
{
    /// <summary>
    /// The Investigation workflow and everything it runs: the agents, the loop between them, what the tools read, the
    /// checkpoint store and the Act step. Needs <c>AddLlm</c> for the chat client and the Neo4j driver.
    /// </summary>
    public static IServiceCollection AddInvestigations(this IServiceCollection services)
    {
        services.AddSingleton<GraphNeighbourhood>();
        // Scoped, like the EventStore they read through.
        services.AddScoped<IInvestigationSource, InvestigationSource>();
        services.AddScoped<Investigator>();
        services.AddScoped<Reviewer>();
        services.AddScoped<InvestigationLoop>();
        services.AddScoped<JsonCheckpointStore, PostgresCheckpointStore>();
        services.AddScoped<IActStep, CloseCaseActStep>();
        services.AddScoped<InvestigationWorkflow>();
        services.AddScoped<IInvestigationRunner, WorkflowInvestigationRunner>();
        return services;
    }
}
