using System.Diagnostics.Metrics;
using Vigil.Agents;
using Vigil.Domain.Telemetry;

namespace Vigil.Api.Cases;

/// <summary>
/// Cost and latency per Investigation, for the dashboard: tokens by agent and direction, the estimated £ from the
/// configured prices, and how long the run to Await Approval took. Recorded once per Investigation from the usage
/// the workflow returns, so a retried agent's tokens are all counted under that agent.
/// </summary>
public sealed class InvestigationMetrics
{
    private readonly LlmOptions _prices;
    private readonly Counter<long> _tokens;
    private readonly Histogram<double> _cost;
    private readonly Histogram<double> _duration;

    // From the host's meter factory, not a static Meter, so each host (and each test host) has its own instruments.
    public InvestigationMetrics(IMeterFactory meters, LlmOptions prices)
    {
        _prices = prices;
        var meter = meters.Create(VigilTelemetry.Name);
        _tokens = meter.CreateCounter<long>("vigil.llm.tokens", "{token}", "LLM tokens used by Investigations, by agent and direction.");
        _cost = meter.CreateHistogram<double>("vigil.llm.cost", "GBP", "Estimated LLM cost of one Investigation, from the configured per-1k-token prices.");
        _duration = meter.CreateHistogram<double>("vigil.investigation.duration", "s", "Time from starting an Investigation to its Await Approval.");
    }

    public void Record(IReadOnlyList<AgentUsage> usage, TimeSpan duration)
    {
        foreach (var call in usage)
        {
            _tokens.Add(call.InputTokens, new("agent", call.AgentId), new("direction", "input"));
            _tokens.Add(call.OutputTokens, new("agent", call.AgentId), new("direction", "output"));
        }

        _cost.Record((double)_prices.CostOf(usage.Sum(u => u.InputTokens), usage.Sum(u => u.OutputTokens)));
        _duration.Record(duration.TotalSeconds);
    }
}
