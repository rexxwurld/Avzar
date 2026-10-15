using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailoring.Application.Documents;

namespace Tailoring.Infrastructure.Storage;

/// <summary>No scanner configured. Files are stored as NotScanned.</summary>
public sealed class NoOpVirusScanner : IVirusScanner
{
    public Task<ScanResult> ScanAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        => Task.FromResult(new ScanResult(ScanOutcome.Disabled));
}

/// <summary>Talks to a clamd daemon with the INSTREAM protocol over TCP. Any failure to get a verdict is "Unavailable", never "Clean".</summary>
public sealed class ClamAvScanner(IOptions<AntivirusOptions> options, ILogger<ClamAvScanner> log) : IVirusScanner
{
    private const int ChunkSize = 64 * 1024;

    public async Task<ScanResult> ScanAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var o = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, o.TimeoutSeconds)));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(o.Host, o.Port, timeout.Token);
            var stream = client.GetStream();

            await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            var header = new byte[4];
            for (var offset = 0; offset < data.Length; offset += ChunkSize)
            {
                var chunk = data.Slice(offset, Math.Min(ChunkSize, data.Length - offset));
                BinaryPrimitives.WriteUInt32BigEndian(header, (uint)chunk.Length);
                await stream.WriteAsync(header, timeout.Token);
                await stream.WriteAsync(chunk, timeout.Token);
            }
            await stream.WriteAsync(new byte[4], timeout.Token); // zero-length chunk ends the stream
            await stream.FlushAsync(timeout.Token);

            var response = new StringBuilder();
            var buf = new byte[256];
            int n;
            while (response.Length < 1024 && (n = await stream.ReadAsync(buf, timeout.Token)) > 0)
            {
                response.Append(Encoding.UTF8.GetString(buf, 0, n));
                if (buf[n - 1] == 0) break;
            }
            return ParseResponse(response.ToString());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "ClamAV scan could not complete.");
            return new ScanResult(ScanOutcome.Unavailable, "scanner_unreachable");
        }
    }

    public static ScanResult ParseResponse(string raw)
    {
        var text = raw.Trim('\0', ' ', '\n', '\r');
        if (text.EndsWith(" OK", StringComparison.Ordinal)) return new ScanResult(ScanOutcome.Clean);
        if (text.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            var body = text["stream:".Length..^" FOUND".Length].Trim();
            return new ScanResult(ScanOutcome.Infected, body.Length == 0 ? "malware" : body);
        }
        return new ScanResult(ScanOutcome.Unavailable, "scanner_error"); // includes "INSTREAM size limit exceeded. ERROR"
    }
}
