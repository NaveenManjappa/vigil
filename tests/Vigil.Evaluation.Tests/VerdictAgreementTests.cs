using Vigil.Agents;
using Vigil.Domain.Datasets;

namespace Vigil.Evaluation.Tests;

public sealed class VerdictAgreementTests
{
    // The GLOSSARY.md definition, one row per Label kind and verdict.
    [Theory]
    [InlineData(FraudKind.Ato, Verdict.LikelyAccountTakeover, true)]
    [InlineData(FraudKind.Ato, Verdict.LikelyRegistrationFraud, false)]
    [InlineData(FraudKind.Ato, Verdict.Inconclusive, false)]
    [InlineData(FraudKind.Registration, Verdict.LikelyRegistrationFraud, true)]
    [InlineData(FraudKind.Registration, Verdict.LikelyAccountTakeover, false)]
    [InlineData(FraudKind.Ring, Verdict.LikelyAccountTakeover, true)]
    [InlineData(FraudKind.Ring, Verdict.LikelyRegistrationFraud, true)]
    [InlineData(FraudKind.Ring, Verdict.LikelyGenuine, false)]
    [InlineData(FraudKind.Evasion, Verdict.LikelyAccountTakeover, true)]
    [InlineData(FraudKind.Evasion, Verdict.Inconclusive, false)]
    public void A_fraud_Label_agrees_only_with_the_fraud_verdicts_that_fit_it(FraudKind kind, Verdict verdict, bool agrees) =>
        Assert.Equal(agrees, VerdictAgreement.Fits(verdict, new Label(Persona.AtoFraudster, kind)));

    [Theory]
    [InlineData(Verdict.LikelyGenuine, true)]
    [InlineData(Verdict.Inconclusive, true)]
    [InlineData(Verdict.LikelyAccountTakeover, false)]
    [InlineData(Verdict.LikelyRegistrationFraud, false)]
    public void A_genuine_Label_agrees_with_LikelyGenuine_or_Inconclusive(Verdict verdict, bool agrees) =>
        Assert.Equal(agrees, VerdictAgreement.Fits(verdict, new Label(Persona.NewPhone, null)));
}
