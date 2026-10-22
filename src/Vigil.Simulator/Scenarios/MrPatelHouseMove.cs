using Vigil.Domain.Events;

namespace Vigil.Simulator.Scenarios;

/// <summary>
/// PROJECT.md Section 3.2: a genuine holder moves house using his usual laptop and IP.
/// No reset, no new device, so no rule should fire.
/// </summary>
public sealed class MrPatelHouseMove : Scenario
{
    private const string Account = "ACC-PATEL";
    private const string Holder = "HOLDER-PATEL";
    private const string UsualLaptop = "D-PATEL-LAPTOP";
    private const string UsualIp = "203.0.113.24"; // documentation range, not a real address

    public override string Name => "patel-house-move";

    public override IReadOnlyList<PortalEvent> Events() =>
    [
        .. Baseline(Account, Holder, UsualLaptop, UsualIp, "GB"),
        Patel(1, EventType.AddressChanged, Anchor, new() { ["NewPostcode"] = "RG1 8AA" }),
        Patel(2, EventType.PhoneChanged, Anchor.AddDays(2), new() { ["NewPhone"] = "+44 7700 900123" }),
    ];

    private PortalEvent Patel(int step, EventType type, DateTimeOffset at, Dictionary<string, string> attributes) =>
        new(IdFor(step), Account, Holder, type, at, UsualLaptop, UsualIp, "GB", "web", attributes);
}
