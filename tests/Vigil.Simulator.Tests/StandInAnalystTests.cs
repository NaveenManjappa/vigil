using Vigil.Domain.Datasets;
using Vigil.Simulator.StandIn;

namespace Vigil.Simulator.Tests;

public sealed class StandInAnalystTests
{
    private static readonly Label Fraud = new(Persona.AtoFraudster, FraudKind.Ato);
    private static readonly Label Genuine = new(Persona.HouseMover, FraudKind: null);

    [Fact]
    public void A_fraud_Label_is_decided_ConfirmedFraud_and_blocks_bank_changes()
    {
        var decision = StandInAnalyst.DecisionFor(Fraud);

        Assert.Equal(("ConfirmedFraud", "BlockBankChanges", "stand-in"), (decision.Outcome, decision.Action, decision.AnalystId));
    }

    [Fact]
    public void A_genuine_Label_is_decided_FalsePositive_with_no_action()
    {
        var decision = StandInAnalyst.DecisionFor(Genuine);

        Assert.Equal(("FalsePositive", "NoAction", "stand-in"), (decision.Outcome, decision.Action, decision.AnalystId));
    }

    private static readonly IReadOnlyDictionary<string, Label> Labels = new Dictionary<string, Label>
    {
        ["ACC-FRAUD"] = Fraud,
        ["ACC-GENUINE"] = Genuine,
    };

    private static CaseListing Case(int n, string account, string status) => new(new Guid(n, 0, 0, new byte[8]), account, status);

    [Fact]
    public void Only_Cases_awaiting_approval_are_decided_each_from_its_own_Accounts_Label()
    {
        var plan = StandInAnalyst.Plan(
            [
                Case(1, "ACC-FRAUD", "AwaitingApproval"),
                Case(2, "ACC-GENUINE", "AwaitingApproval"),
                Case(3, "ACC-FRAUD", "Closed"),
                Case(4, "ACC-GENUINE", "Open"),
                Case(5, "ACC-FRAUD", "Investigating"),
            ],
            Labels);

        Assert.Equal(
            [(Case(1, "", "").Id, "ConfirmedFraud"), (Case(2, "", "").Id, "FalsePositive")],
            plan.ToDecide.Select(d => (d.CaseId, d.Decision.Outcome)));
    }

    [Fact]
    public void Cases_still_open_or_investigating_are_counted_as_not_ready()
    {
        var plan = StandInAnalyst.Plan(
            [Case(1, "ACC-FRAUD", "Open"), Case(2, "ACC-GENUINE", "Investigating"), Case(3, "ACC-FRAUD", "Closed")],
            Labels);

        Assert.Empty(plan.ToDecide);
        Assert.Equal(2, plan.NotReady);
    }

    [Fact]
    public void A_Case_on_an_Account_outside_the_Dataset_is_left_for_a_human_and_named()
    {
        var plan = StandInAnalyst.Plan([Case(1, "ACC-MARGARET", "AwaitingApproval")], Labels);

        Assert.Empty(plan.ToDecide);
        Assert.Equal(["ACC-MARGARET"], plan.Unlabelled);
    }
}
