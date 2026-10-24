using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vigil.Api.Llm;

namespace Vigil.Api.Tests;

public sealed class TokenLoggingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly CapturingLoggerProvider _logs = new();

    private IChatClient Resolve(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection().AddLogging(l => l.AddProvider(_logs));
        services.AddLlm(configuration);
        return services.BuildServiceProvider().GetRequiredService<IChatClient>();
    }

    private CapturedLog TokenLog() => Assert.Single(_logs.Entries, e => e.Properties.ContainsKey("InputTokens"));

    [Fact]
    public async Task Every_call_through_the_registered_client_logs_its_tokens()
    {
        var client = Resolve();

        await client.GetResponseAsync("hello", cancellationToken: Ct);

        var log = TokenLog();
        Assert.Equal(FakeChatClient.ModelId, log.Properties["Model"]);
        Assert.Equal(0L, log.Properties["InputTokens"]);
        Assert.Equal(0L, log.Properties["OutputTokens"]);
    }

    [Fact]
    public async Task A_call_logs_input_and_output_tokens_with_the_estimated_cost()
    {
        var logger = new LoggerFactory([_logs]).CreateLogger<TokenLoggingChatClient>();
        var prices = new LlmOptions { InputPricePer1kTokens = 0.0003m, OutputPricePer1kTokens = 0.0012m };
        var client = new TokenLoggingChatClient(new StubChatClient(input: 1200, output: 300), prices, logger);

        await client.GetResponseAsync("hello", cancellationToken: Ct);

        var log = TokenLog();
        Assert.Equal("stub-model", log.Properties["Model"]);
        Assert.Equal(1200L, log.Properties["InputTokens"]);
        Assert.Equal(300L, log.Properties["OutputTokens"]);
        // 1.2k x £0.0003 + 0.3k x £0.0012
        Assert.Equal(0.00072m, log.Properties["EstimatedCost"]);
    }

    [Fact]
    public async Task A_streamed_call_logs_its_tokens_once_the_stream_ends()
    {
        var logger = new LoggerFactory([_logs]).CreateLogger<TokenLoggingChatClient>();
        var client = new TokenLoggingChatClient(new StubChatClient(input: 50, output: 7), new LlmOptions(), logger);

        await foreach (var _ in client.GetStreamingResponseAsync("hello", cancellationToken: Ct)) { }

        var log = TokenLog();
        Assert.Equal(50L, log.Properties["InputTokens"]);
        Assert.Equal(7L, log.Properties["OutputTokens"]);
    }

    private sealed class StubChatClient(long input, long output) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                ModelId = "stub-model",
                Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output },
            });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}

internal sealed record CapturedLog(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties);

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyCollection<CapturedLog> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new Logger(_entries);

    public void Dispose() { }

    private sealed class Logger(ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            entries.Enqueue(new CapturedLog(logLevel, formatter(state, exception), properties.ToDictionary(p => p.Key, p => p.Value)));
        }
    }
}
