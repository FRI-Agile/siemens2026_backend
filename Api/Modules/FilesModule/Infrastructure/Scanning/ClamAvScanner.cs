using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace FilesModule.Infrastructure.Scanning;

/// <summary>
/// Scans with clamd over its TCP protocol: the ClamAV container in the pod on the VM (container-deployment.md §1.4) and
/// in the Aspire stack (aspire-dev-stack.md §4). The bytes are streamed with <c>INSTREAM</c>, so nothing is written to
/// disk on the way.
/// </summary>
/// <param name="endpoint">clamd's address, <c>tcp://host:port</c>.</param>
internal sealed class ClamAvScanner(Uri endpoint) : IMalwareScanner
{
    /// <summary>The chunk size; clamd's default <c>StreamMaxLength</c> limits the whole stream, not a chunk.</summary>
    private const int ChunkSize = 64 * 1024;

    /// <summary>How long one scan may take, from connecting to the verdict.</summary>
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMinutes(2);

    public async Task<ScanVerdict> ScanAsync(Stream content, CancellationToken ct = default)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ScanTimeout);

        using TcpClient client = new();
        await client.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token);
        await using NetworkStream stream = client.GetStream();

        // The z-prefixed form is NUL-terminated, so the reply is read up to one NUL without newline parsing.
        await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkSize + 4);
        try
        {
            // Each chunk is its length as a 4-byte big-endian integer, then the bytes; a zero length ends the stream.
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(4, ChunkSize), timeout.Token)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)read);
                await stream.WriteAsync(buffer.AsMemory(0, read + 4), timeout.Token);
            }

            BinaryPrimitives.WriteUInt32BigEndian(buffer, 0);
            await stream.WriteAsync(buffer.AsMemory(0, 4), timeout.Token);

            return Parse(await ReadReplyAsync(stream, timeout.Token));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Turns clamd's reply into a verdict: <c>stream: OK</c>, or <c>stream: &lt;signature&gt; FOUND</c>.</summary>
    /// <exception cref="InvalidOperationException">clamd answered with an error, e.g. the stream exceeded its limit.</exception>
    internal static ScanVerdict Parse(string reply)
    {
        if (reply.EndsWith(" OK", StringComparison.Ordinal))
            return ScanVerdict.Passed;

        if (reply.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            string signature = reply["stream: ".Length..^" FOUND".Length];
            return new ScanVerdict(false, signature);
        }

        // An ERROR reply ("INSTREAM size limit exceeded", a clamd still loading) is not a verdict; the file stays unscanned
        // and is tried again.
        throw new InvalidOperationException($"clamd answered '{reply}'.");
    }

    private static async Task<string> ReadReplyAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[256];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length), ct);
            if (read == 0)
                break;
            length += read;
            if (Array.IndexOf(buffer, (byte)0, 0, length) >= 0)
                break;
        }

        return Encoding.ASCII.GetString(buffer, 0, length).TrimEnd('\0', '\n');
    }
}
