namespace Vigil.Api.Llm;

public enum LlmProvider
{
    Fake,
    AzureOpenAI,
}

/// <summary>The <c>Llm</c> configuration section: which provider serves <c>IChatClient</c>, and what a token costs.</summary>
public sealed class LlmOptions
{
    public const string Section = "Llm";

    public LlmProvider Provider { get; set; } = LlmProvider.Fake;

    /// <summary>The Azure OpenAI resource, e.g. <c>https://vigil.openai.azure.com/</c>; <c>/openai/v1/</c> is appended.</summary>
    public string? Endpoint { get; set; }

    /// <summary>The Azure deployment name, which the v1 endpoint takes as the model name.</summary>
    public string? Deployment { get; set; }

    /// <summary>Supplied by the AppHost's <c>azure-openai-key</c> secret parameter, never by appsettings.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Pounds per 1,000 input tokens, for estimating spend against the budget.</summary>
    public decimal InputPricePer1kTokens { get; set; }

    /// <summary>Pounds per 1,000 output tokens.</summary>
    public decimal OutputPricePer1kTokens { get; set; }

    /// <summary>The estimated £ for a number of tokens, at the configured prices.</summary>
    public decimal CostOf(long inputTokens, long outputTokens) =>
        inputTokens / 1000m * InputPricePer1kTokens + outputTokens / 1000m * OutputPricePer1kTokens;
}
