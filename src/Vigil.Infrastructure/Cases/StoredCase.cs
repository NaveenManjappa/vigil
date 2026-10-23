using Vigil.Domain.Cases;
using Vigil.Infrastructure.Alerts;

namespace Vigil.Infrastructure.Cases;

/// <summary>
/// The work item an analyst picks up for one Alert. Band and Score are read from the Alert rather than copied,
/// so an Alert that rises to Critical reprioritises its Case without the Case being touched.
/// </summary>
public sealed class StoredCase
{
    public Guid CaseId { get; init; }
    public Guid AlertId { get; init; }
    public StoredAlert Alert { get; init; } = null!;
    public CaseStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; init; }
}
