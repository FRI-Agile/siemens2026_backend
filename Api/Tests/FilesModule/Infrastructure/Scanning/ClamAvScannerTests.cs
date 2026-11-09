using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FilesModule.Infrastructure.Scanning;

namespace Tests.FilesModule.Infrastructure.Scanning;

/// <summary>clamd's INSTREAM protocol, against a listener that speaks it.</summary>
public class ClamAvScannerTests
{
    [Fact]
    public void Parses_clean_infected_and_error_replies()
    {
        Assert.Equal(ScanVerdict.Passed, ClamAvScanner.Parse("stream: OK"));
        Assert.Equal(new ScanVerdict(false, "Win.Test.EICAR_HDB-1"), ClamAvScanner.Parse("stream: Win.Test.EICAR_HDB-1 FOUND"));
        Assert.Throws<InvalidOperationException>(() => ClamAvScanner.Parse("INSTREAM size limit exceeded. ERROR"));
    }

    [Fact]
    public async Task Streams_the_bytes_in_length_prefixed_chunks_and_reads_the_verdict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        byte[] content = new byte[150_000]; // more than one 64 KiB chunk
        Random.Shared.NextBytes(content);

        Task<(string Command, byte[] Received)> clamd = ServeOnceAsync(listener, "stream: Eicar-Signature FOUND", ct);
        ScanVerdict verdict = await new ClamAvScanner(new Uri($"tcp://127.0.0.1:{port}")).ScanAsync(new MemoryStream(content), ct);
        (string command, byte[] received) = await clamd;

        Assert.Equal("zINSTREAM", command);
        Assert.Equal(content, received);
        Assert.Equal(new ScanVerdict(false, "Eicar-Signature"), verdict);
    }

    /// <summary>Accepts one connection, reads a z-command and its chunks up to the zero length, and answers.</summary>
    private static async Task<(string, byte[])> ServeOnceAsync(TcpListener listener, string reply, CancellationToken ct)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(ct);
        await using NetworkStream stream = client.GetStream();

        List<byte> command = [];
        byte[] one = new byte[1];
        while (await stream.ReadAsync(one, ct) == 1 && one[0] != 0)
            command.Add(one[0]);

        using MemoryStream received = new();
        byte[] length = new byte[4];
        while (true)
        {
            await stream.ReadExactlyAsync(length, ct);
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(length);
            if (size == 0)
                break;
            byte[] chunk = new byte[size];
            await stream.ReadExactlyAsync(chunk, ct);
            received.Write(chunk);
        }

        await stream.WriteAsync(Encoding.ASCII.GetBytes(reply + "\0"), ct);
        return (Encoding.ASCII.GetString([.. command]), received.ToArray());
    }
}
