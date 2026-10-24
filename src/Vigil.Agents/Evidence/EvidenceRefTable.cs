namespace Vigil.Agents;

/// <summary>
/// One Investigation's Evidence Refs: short labels the agents cite instead of EventIds,
/// <c>E-n</c> for a Portal Event and <c>G-n</c> for a graph fact. Refs are only ever added, never changed,
/// so a ref an agent cited keeps meaning what it meant when it was shown.
/// </summary>
public sealed class EvidenceRefTable
{
    private readonly List<KeyValuePair<string, IReadOnlyList<Guid>>> _entries = [];
    private readonly Dictionary<string, IReadOnlyList<Guid>> _eventIdsByRef = new();
    private readonly Dictionary<Guid, string> _refByEvent = new();
    private int _events;
    private int _graphFacts;

    /// <summary>Every ref with the EventIds it stands for, in the order added: what an Audit Entry records.</summary>
    public IReadOnlyList<KeyValuePair<string, IReadOnlyList<Guid>>> Entries => _entries;

    public string AddEvent(Guid eventId)
    {
        // The same event can come back from Enrich and again from a timeline tool call;
        // one ref per event stops the agents citing two labels for one fact.
        if (_refByEvent.TryGetValue(eventId, out var existing))
            return existing;

        return _refByEvent[eventId] = Add($"E-{++_events}", [eventId]);
    }

    public string AddGraphFact(IReadOnlyList<Guid> eventIds) => Add($"G-{++_graphFacts}", eventIds);

    public IReadOnlyList<Guid> Resolve(string evidenceRef) => _eventIdsByRef[evidenceRef];

    /// <summary>The cited refs this table never issued, each once: a non-empty answer rejects the Finding before review.</summary>
    public IReadOnlyList<string> FindUnknown(IEnumerable<string> citedRefs) =>
        citedRefs.Where(r => !_eventIdsByRef.ContainsKey(r)).Distinct().ToList();

    private string Add(string evidenceRef, IReadOnlyList<Guid> eventIds)
    {
        _eventIdsByRef[evidenceRef] = eventIds;
        _entries.Add(new(evidenceRef, eventIds));
        return evidenceRef;
    }
}
