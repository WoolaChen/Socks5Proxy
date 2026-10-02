using Microsoft.Extensions.Logging;

namespace Socks5Proxy.Server;

/// <summary>
/// Pumps bytes bidirectionally between two streams until either side closes,
/// an error occurs, or the connection is idle for longer than <paramref name="idleTimeout"/>.
/// </summary>
public static class Socks5Relay
{
    private const int BufferSize = 16 * 1024;

    public static async Task RelayAsync(
        Stream left,
        Stream right,
        TimeSpan idleTimeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var idleCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idleCts.Token);
        var lastActivityTicks = Environment.TickCount64;

        void NoteActivity() => Interlocked.Exchange(ref lastActivityTicks, Environment.TickCount64);

        using var idleMonitor = StartIdleMonitor(idleTimeout, () => Interlocked.Read(ref lastActivityTicks), idleCts);

        var pumpLeftToRight = PumpAsync(left, right, NoteActivity, logger, linkedCts.Token);
        var pumpRightToLeft = PumpAsync(right, left, NoteActivity, logger, linkedCts.Token);

        await Task.WhenAny(pumpLeftToRight, pumpRightToLeft).ConfigureAwait(false);
        linkedCts.Cancel();

        try
        {
            await Task.WhenAll(pumpLeftToRight, pumpRightToLeft).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected once either pump completes and we cancel the other.
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Relay pump ended with an exception.");
        }
    }

    private static IDisposable StartIdleMonitor(TimeSpan idleTimeout, Func<long> getLastActivity, CancellationTokenSource idleCts)
    {
        var timer = new Timer(_ =>
        {
            var elapsedMs = Environment.TickCount64 - getLastActivity();
            if (elapsedMs >= idleTimeout.TotalMilliseconds)
            {
                idleCts.Cancel();
            }
        }, null, idleTimeout, TimeSpan.FromSeconds(1));

        return timer;
    }

    private static async Task PumpAsync(Stream input, Stream output, Action onActivity, ILogger logger, CancellationToken cancellationToken)
    {
        var buffer = new byte[BufferSize];
        try
        {
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                onActivity();
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                onActivity();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Relay stream closed.");
        }
        catch (ObjectDisposedException)
        {
            // Underlying socket/stream was disposed by the peer pump; ignore.
        }
    }
}
