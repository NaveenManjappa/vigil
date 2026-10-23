using Vigil.Agents;
using Vigil.Api.Investigations;
using Vigil.Graph;

// In the DI namespace, like the framework's own Add* extensions, so Program.cs needs no extra using.
namespace Microsoft.Extensions.DependencyInjection;

public static class InvestigationRegistration
{
    /// <summary>The Investigator, the Reviewer, the loop between them, and what the tools read. Needs <c>AddLlm</c> for the chat client and the Neo4j driver.</summary>
    public static IServiceCollection AddInvestigations(this IServiceCollection services)
    {
        services.AddSingleton<GraphNeighbourhood>();
        // Scoped, like the EventStore they read through.
        services.AddScoped<IInvestigationSource, InvestigationSource>();
        services.AddScoped<Investigator>();
        services.AddScoped<Reviewer>();
        services.AddScoped<InvestigationLoop>();
        return services;
    }
}
