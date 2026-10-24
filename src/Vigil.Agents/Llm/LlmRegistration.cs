using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI;
using Vigil.Agents;
using Vigil.Domain.Telemetry;

// In the DI namespace, like the framework's own Add* extensions, so Program.cs needs no extra using.
namespace Microsoft.Extensions.DependencyInjection;

public static class LlmRegistration
{
    /// <summary>
    /// Registers the one <see cref="IChatClient"/> the agents use, chosen by <c>Llm:Provider</c> (ADR-0009). Bad
    /// settings throw here, at startup, rather than on the first Investigation hours later.
    /// </summary>
    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(LlmOptions.Section);
        // Checked before binding, because the binder's own enum error does not list the valid providers.
        var provider = ParseProvider(section[nameof(LlmOptions.Provider)]);
        var options = section.Get<LlmOptions>() ?? new LlmOptions();

        var client = provider switch
        {
            LlmProvider.AzureOpenAI => AzureOpenAI(options),
            _ => new FakeChatClient(),
        };

        services.AddChatClient(client)
            .Use((inner, sp) => new TokenLoggingChatClient(inner, options, sp.GetRequiredService<ILogger<TokenLoggingChatClient>>()))
            // Below the agents' function invoker, which finds this layer's ActivitySource and puts its execute_tool
            // spans beside each chat span. Prompts stay out of the trace: they are masked, but would bloat it.
            .UseOpenTelemetry(sourceName: VigilTelemetry.LlmSourceName, configure: c => c.EnableSensitiveData = false);
        services.AddSingleton(options);
        return services;
    }

    private static LlmProvider ParseProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return LlmProvider.Fake;
        if (Enum.TryParse<LlmProvider>(value, ignoreCase: true, out var provider) && Enum.IsDefined(provider))
            return provider;
        throw new InvalidOperationException(
            $"Llm:Provider '{value}' is not supported. Use one of: {string.Join(", ", Enum.GetNames<LlmProvider>())}.");
    }

    private static IChatClient AzureOpenAI(LlmOptions options)
    {
        var endpoint = Required(options.Endpoint, nameof(LlmOptions.Endpoint));
        var deployment = Required(options.Deployment, nameof(LlmOptions.Deployment));
        var apiKey = Required(options.ApiKey, nameof(LlmOptions.ApiKey));

        // Azure's v1 endpoint speaks the plain OpenAI protocol, so the stable OpenAI SDK serves it (ADR-0009).
        var v1 = new Uri($"{endpoint.TrimEnd('/')}/openai/v1/");
        var openAi = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = v1 });
        return openAi.GetChatClient(deployment).AsIChatClient();
    }

    private static string Required(string? value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Llm:{key} is required when Llm:Provider is AzureOpenAI. Set it in configuration, or use Llm:Provider Fake.")
            : value;
}
