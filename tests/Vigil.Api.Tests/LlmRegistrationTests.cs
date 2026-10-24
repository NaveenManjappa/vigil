using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vigil.Agents;

namespace Vigil.Api.Tests;

// No containers: the provider switch is pure registration, so these run without Docker or network.
public sealed class LlmRegistrationTests
{
    private static IChatClient Resolve(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection().AddLogging();
        services.AddLlm(configuration);
        return services.BuildServiceProvider().GetRequiredService<IChatClient>();
    }

    private static string Provider(IChatClient client) => client.GetService<ChatClientMetadata>()!.ProviderName!;

    [Fact]
    public void With_no_llm_config_the_fake_provider_is_used()
    {
        Assert.Equal("fake", Provider(Resolve()));
    }

    [Fact]
    public void Azure_openai_is_used_through_its_v1_endpoint_with_the_deployment_as_model()
    {
        var client = Resolve(
            ("Llm:Provider", "AzureOpenAI"),
            ("Llm:Endpoint", "https://vigil-test.openai.azure.com/"),
            ("Llm:Deployment", "gpt-4.1-mini"),
            ("Llm:ApiKey", "test-key"));

        var metadata = client.GetService<ChatClientMetadata>()!;
        Assert.Equal("openai", metadata.ProviderName);
        Assert.Equal(new Uri("https://vigil-test.openai.azure.com/openai/v1/"), metadata.ProviderUri);
        Assert.Equal("gpt-4.1-mini", metadata.DefaultModelId);
    }

    [Fact]
    public void An_unknown_provider_fails_with_a_message_naming_the_valid_ones()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(("Llm:Provider", "Ollama")));

        Assert.Contains("'Ollama'", error.Message);
        Assert.Contains("AzureOpenAI", error.Message);
        Assert.Contains("Fake", error.Message);
    }

    [Theory]
    [InlineData("Llm:Endpoint")]
    [InlineData("Llm:Deployment")]
    [InlineData("Llm:ApiKey")]
    public void Azure_openai_with_a_missing_setting_fails_with_a_message_naming_it(string missing)
    {
        (string, string)[] azure =
        [
            ("Llm:Provider", "AzureOpenAI"),
            ("Llm:Endpoint", "https://vigil-test.openai.azure.com/"),
            ("Llm:Deployment", "gpt-4.1-mini"),
            ("Llm:ApiKey", "test-key"),
        ];

        var error = Assert.Throws<InvalidOperationException>(() => Resolve(azure.Where(s => s.Item1 != missing).ToArray()));

        Assert.Contains(missing, error.Message);
    }
}
