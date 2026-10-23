namespace Vigil.Api.Cases;

/// <summary>The budget guard on LLM spend, bound from the <c>Investigation</c> config section.</summary>
public sealed class InvestigationOptions
{
    /// <summary>Off for bulk Dataset replays, so they never call the LLM.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Investigations started per UTC day; later Cases wait Open, marked not investigated.</summary>
    public int DailyLimit { get; init; } = 20;
}
