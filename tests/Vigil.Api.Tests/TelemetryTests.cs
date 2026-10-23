using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Vigil.Agents;
using Vigil.Domain.Cases;

namespace Vigil.Api.Tests;

/// <summary>
/// What an engineer on call sees: one trace from the ingest request to both agents, a decision trace linked to it,
/// and token, cost and duration metrics. Listeners are process-wide, so activities are picked out by trace id and
/// instruments by the host's own meter factory.
/// </summary>
public sealed class TelemetryTests(MeteredApiFactory factory) : IClassFixture<MeteredApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static readonly ActivitySource TestSource = new("Vigil.Api.Tests");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_investigation_joins_the_ingest_trace_and_the_decision_links_back_to_it()
    {
        using var activities = new ActivityRecorder();

        // The caller's trace context, as the simulator's HttpClient would send it.
        // Only the ingest request carries it; the polling below and the decision are separate requests.
        ActivityTraceId ingestTrace;
        var ingester = factory.CreateClient();
        using (var caller = TestSource.StartActivity("simulator post")!)
        {
            ingestTrace = caller.TraceId;
            ingester.DefaultRequestHeaders.Add("traceparent", caller.Id);
            await InvestigationQueueTests.TakeOverToHighAsync(ingester, "ACC-TRACE");
        }
        var client = factory.CreateClient();
        var caseId = (await InvestigationQueueTests.CaseEventuallyAsync(client, "ACC-TRACE", c => c.Status == CaseStatus.AwaitingApproval)).Id;
        var investigation = await activities.EventuallyAsync(a => a.OperationName == "investigate case" && Tag(a, "vigil.case_id") == caseId.ToString());

        var inIngestTrace = activities.Stopped.Where(a => a.TraceId == ingestTrace).ToList();
        Assert.Equal(ingestTrace, investigation.TraceId);
        Assert.Contains(inIngestTrace, a => a.DisplayName == "POST /events" && a.Kind == ActivityKind.Server);
        Assert.Contains(inIngestTrace, a => a.DisplayName == "graph.project");
        Assert.Contains(inIngestTrace, a => a.DisplayName == "graph.rules");
        Assert.Contains(inIngestTrace, a => a.DisplayName == "detect");
        Assert.Contains(inIngestTrace, a => a.DisplayName == "workflow.session");
        Assert.Contains(inIngestTrace, a => a.DisplayName == $"invoke_agent {Investigator.AgentName}({Investigator.AgentId})");
        Assert.Contains(inIngestTrace, a => a.DisplayName == "execute_tool QueryGraphNeighbourhood");
        Assert.Contains(inIngestTrace, a => a.DisplayName == $"invoke_agent {Reviewer.AgentName}({Reviewer.AgentId})");
        // Polling for the Case sent no trace context, so it is in a trace of its own.
        Assert.DoesNotContain(inIngestTrace, a => a.DisplayName == "GET /cases");

        (await client.PostAsJsonAsync($"/cases/{caseId}/decision",
            new AnalystDecision(DecisionOutcome.FalsePositive, Recommendation.NoAction, "analyst-7", null), Json, Ct)).EnsureSuccessStatusCode();

        var decision = await activities.EventuallyAsync(a => a.OperationName == "decide case" && Tag(a, "vigil.case_id") == caseId.ToString());
        Assert.NotEqual(ingestTrace, decision.TraceId);
        var link = Assert.Single(decision.Links);
        Assert.Equal(ingestTrace, link.Context.TraceId);
        Assert.Equal(investigation.SpanId, link.Context.SpanId);
    }

    [Fact]
    public async Task An_investigation_records_tokens_per_agent_its_estimated_cost_and_its_duration()
    {
        using var metrics = new MetricRecorder(factory.Services.GetRequiredService<IMeterFactory>());
        var client = factory.CreateClient();

        await InvestigationQueueTests.TakeOverToHighAsync(client, "ACC-METERED");
        await InvestigationQueueTests.CaseEventuallyAsync(client, "ACC-METERED", c => c.Status == CaseStatus.AwaitingApproval);
        await metrics.EventuallyAsync("vigil.investigation.duration");

        // The metered Fake reports 100 input and 20 output tokens a call: the Investigator calls twice (its tool
        // call, then its Finding) and the Reviewer once.
        Assert.Equal(200, metrics.Sum("vigil.llm.tokens", ("agent", Investigator.AgentId), ("direction", "input")));
        Assert.Equal(40, metrics.Sum("vigil.llm.tokens", ("agent", Investigator.AgentId), ("direction", "output")));
        Assert.Equal(100, metrics.Sum("vigil.llm.tokens", ("agent", Reviewer.AgentId), ("direction", "input")));
        Assert.Equal(20, metrics.Sum("vigil.llm.tokens", ("agent", Reviewer.AgentId), ("direction", "output")));
        // 0.3k input x £0.0003 + 0.06k output x £0.0012, from appsettings.json.
        Assert.Equal(0.000162, metrics.Sum("vigil.llm.cost"), precision: 9);
        Assert.Single(metrics.Measurements("vigil.investigation.duration"));
    }

    private static string? Tag(Activity a, string key) => a.GetTagItem(key)?.ToString();

    private sealed class ActivityRecorder : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly ConcurrentQueue<Activity> _stopped = new();

        public ActivityRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyCollection<Activity> Stopped => _stopped;

        public async Task<Activity> EventuallyAsync(Func<Activity, bool> match)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (true)
            {
                if (_stopped.FirstOrDefault(match) is { } found) return found;
                if (DateTime.UtcNow > deadline) Assert.Fail("The expected activity never stopped.");
                await Task.Delay(50, Ct);
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class MetricRecorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = new();

        public MetricRecorder(IMeterFactory hostMeters)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Vigil" && instrument.Meter.Scope == hostMeters) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
            _listener.Start();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
            _measurements.Enqueue((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));

        public IEnumerable<double> Measurements(string instrument, params (string Key, string Value)[] tags) =>
            _measurements
                .Where(m => m.Instrument == instrument && tags.All(t => m.Tags.GetValueOrDefault(t.Key)?.ToString() == t.Value))
                .Select(m => m.Value);

        public double Sum(string instrument, params (string Key, string Value)[] tags) => Measurements(instrument, tags).Sum();

        public async Task EventuallyAsync(string instrument)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!Measurements(instrument).Any())
            {
                if (DateTime.UtcNow > deadline) Assert.Fail($"{instrument} was never recorded.");
                await Task.Delay(50, Ct);
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
