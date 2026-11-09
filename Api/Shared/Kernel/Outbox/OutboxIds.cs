using System.Numerics;

namespace Kernel.Outbox;

/// <summary>
/// Ids for outbox rows that are strictly increasing within the process, so ordering by id is the order changes
/// happened in. <see cref="Guid.CreateVersion7()"/> orders only by millisecond and fills the rest at random: two
/// changes of one save would otherwise be delivered in either order.
/// </summary>
/// <remarks>
/// Still version 7 UUIDs as far as storage cares: the timestamp leads, and within one millisecond, or after the clock
/// stepped back, the next id is the previous one plus one. PostgreSQL and <see cref="Guid.CompareTo(Guid)"/> both order
/// a UUID by its big-endian bytes, which is the order this keeps.
/// </remarks>
internal static class OutboxIds
{
    private static readonly Lock Gate = new();
    private static BigInteger _last;

    /// <summary>The next id, greater than every id this process issued before.</summary>
    public static Guid Next()
    {
        BigInteger candidate = ToNumber(Guid.CreateVersion7());
        lock (Gate)
        {
            _last = candidate > _last ? candidate : _last + 1;
            return FromNumber(_last);
        }
    }

    private static BigInteger ToNumber(Guid id) =>
        new(id.ToByteArray(bigEndian: true), isUnsigned: true, isBigEndian: true);

    private static Guid FromNumber(BigInteger value)
    {
        byte[] bytes = new byte[16];
        value.TryWriteBytes(bytes, out int written, isUnsigned: true, isBigEndian: true);
        // A value with leading zero bytes is written short; right-align it.
        if (written < 16)
        {
            Array.Copy(bytes, 0, bytes, 16 - written, written);
            Array.Clear(bytes, 0, 16 - written);
        }

        return new Guid(bytes, bigEndian: true);
    }
}
