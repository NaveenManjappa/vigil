using Vigil.Agents;
using Vigil.Domain.Datasets;

namespace Vigil.Evaluation;

/// <summary>Whether a Finding's verdict fits the Account's Label, as GLOSSARY.md defines Verdict Agreement.</summary>
public static class VerdictAgreement
{
    public static bool Fits(Verdict verdict, Label label) => label.FraudKind switch
    {
        FraudKind.Ato => verdict == Verdict.LikelyAccountTakeover,
        FraudKind.Registration => verdict == Verdict.LikelyRegistrationFraud,
        // A ring or an evasion can be run through either kind of fraud, so either fraud verdict is right.
        FraudKind.Ring or FraudKind.Evasion => verdict is Verdict.LikelyAccountTakeover or Verdict.LikelyRegistrationFraud,
        // Inconclusive counts for genuine: it sends the Case to the analyst without accusing the Holder.
        null => verdict is Verdict.LikelyGenuine or Verdict.Inconclusive,
        _ => throw new ArgumentOutOfRangeException(nameof(label), label.FraudKind, "No Verdict Agreement rule for this kind of fraud."),
    };
}
