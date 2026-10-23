namespace Vigil.Agents.Tests;

public sealed class EvidenceRefTableTests
{
    private static readonly Guid Reset = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Mfa = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid DerekLink = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid MargaretLink = Guid.Parse("00000000-0000-0000-0000-000000000004");

    [Fact]
    public void Portal_Events_and_graph_facts_are_numbered_separately_and_resolve_to_their_EventIds()
    {
        var refs = new EvidenceRefTable();

        var e1 = refs.AddEvent(Reset);
        var g1 = refs.AddGraphFact([DerekLink, MargaretLink]);
        var e2 = refs.AddEvent(Mfa);

        Assert.Equal(("E-1", "G-1", "E-2"), (e1, g1, e2));
        Assert.Equal([Reset], refs.Resolve("E-1"));
        Assert.Equal([Mfa], refs.Resolve("E-2"));
        Assert.Equal([DerekLink, MargaretLink], refs.Resolve("G-1"));
    }

    [Fact]
    public void An_event_shown_again_by_a_later_tool_call_keeps_its_first_ref()
    {
        var refs = new EvidenceRefTable();
        refs.AddEvent(Reset);
        refs.AddEvent(Mfa);

        var again = refs.AddEvent(Reset);

        Assert.Equal("E-1", again);
        Assert.Equal("E-3", refs.AddEvent(DerekLink));
    }

    [Fact]
    public void Validation_reports_every_cited_ref_that_is_not_in_the_table()
    {
        var refs = new EvidenceRefTable();
        refs.AddEvent(Reset);
        refs.AddGraphFact([DerekLink, MargaretLink]);

        var unknown = refs.FindUnknown(["E-1", "E-7", "G-1", "G-2", "E-7", "evidence"]);

        Assert.Equal(["E-7", "G-2", "evidence"], unknown);
        Assert.Empty(refs.FindUnknown(["G-1", "E-1"]));
    }

    [Fact]
    public void The_table_lists_every_ref_with_its_EventIds_in_the_order_added()
    {
        var refs = new EvidenceRefTable();
        refs.AddEvent(Reset);
        refs.AddGraphFact([DerekLink, MargaretLink]);

        Assert.Equal(
            [new KeyValuePair<string, IReadOnlyList<Guid>>("E-1", [Reset]), new("G-1", [DerekLink, MargaretLink])],
            refs.Entries);
    }
}
