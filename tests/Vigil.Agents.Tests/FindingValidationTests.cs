namespace Vigil.Agents.Tests;

public sealed class FindingValidationTests
{
    private readonly EvidenceRefTable _refs = new();

    public FindingValidationTests()
    {
        _refs.AddEvent(Guid.NewGuid());
        _refs.AddGraphFact([Guid.NewGuid()]);
    }

    private static Finding Valid(string summary = "A new device took over the Account.", IReadOnlyList<string>? evidence = null) =>
        new(Verdict.LikelyAccountTakeover, Confidence.High, summary, Recommendation.BlockBankChanges, evidence ?? ["E-1", "G-1"]);

    [Fact]
    public void A_Finding_within_the_contract_citing_known_refs_passes()
    {
        Assert.Empty(FindingValidation.Check(Valid(), _refs));
    }

    [Fact]
    public void A_summary_of_one_hundred_and_twenty_words_passes_and_one_more_is_rejected()
    {
        var limit = string.Join(" ", Enumerable.Repeat("word", 120));

        Assert.Empty(FindingValidation.Check(Valid(limit), _refs));
        var reason = Assert.Single(FindingValidation.Check(Valid(limit + " over"), _refs));
        Assert.Contains("121 words", reason);
    }

    [Fact]
    public void Every_unknown_ref_is_named_in_the_rejection()
    {
        var reason = Assert.Single(FindingValidation.Check(Valid(evidence: ["E-1", "E-9", "G-4"]), _refs));

        Assert.Contains("E-9, G-4", reason);
    }

    [Fact]
    public void Missing_fields_and_values_outside_the_contract_are_each_rejected()
    {
        // What a reply missing summary and evidence, with a numeric verdict, deserialises to.
        var finding = new Finding((Verdict)7, Confidence.Low, null!, (Recommendation)9, null!);

        var reasons = FindingValidation.Check(finding, _refs);

        Assert.Equal(4, reasons.Count);
        Assert.Contains(reasons, r => r.Contains("verdict"));
        Assert.Contains(reasons, r => r.Contains("recommendation"));
        Assert.Contains(reasons, r => r.Contains("summary"));
        Assert.Contains(reasons, r => r.Contains("evidence"));
    }

    [Fact]
    public void A_Finding_that_cites_no_evidence_is_rejected()
    {
        var reason = Assert.Single(FindingValidation.Check(Valid(evidence: []), _refs));

        Assert.Contains("evidence", reason);
    }
}
