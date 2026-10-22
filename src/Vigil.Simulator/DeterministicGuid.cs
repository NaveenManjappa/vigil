using System.Security.Cryptography;
using System.Text;

namespace Vigil.Simulator;

/// <summary>
/// RFC 9562 version-5 (name-based, SHA-1) GUIDs. The same scenario step always gets the same EventId,
/// so replaying a scenario exercises ingestion idempotency instead of creating duplicates.
/// </summary>
public static class DeterministicGuid
{
    // Fixed namespace for all Vigil simulator IDs (itself an arbitrary random GUID).
    private static readonly Guid Namespace = new("6b1f3a0e-9c52-4d8e-a7e1-2f4c8b9d0e31");

    public static Guid Create(string name)
    {
        Span<byte> input = stackalloc byte[16 + Encoding.UTF8.GetByteCount(name)];
        Namespace.TryWriteBytes(input, bigEndian: true, out _);
        Encoding.UTF8.GetBytes(name, input[16..]);

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(input, hash);

        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC variant
        return new Guid(bytes, bigEndian: true);
    }
}
