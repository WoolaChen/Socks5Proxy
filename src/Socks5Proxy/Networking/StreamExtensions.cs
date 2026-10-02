using Socks5Proxy.Protocol;

namespace Socks5Proxy.Networking;

/// <summary>
/// Helper extension methods for reading fixed-size frames off a <see cref="Stream"/>
/// with strict end-of-stream and cancellation handling.
/// </summary>
public static class StreamExtensions
{
    /// <summary>
    /// Reads exactly <paramref name="length"/> bytes, throwing a
    /// <see cref="Socks5ProtocolException"/> if the peer closes the connection early.
    /// </summary>
    public static async Task<byte[]> ReadExactAsync(this Stream stream, int length, CancellationToken cancellationToken)
    {
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, length - read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                throw new Socks5ProtocolException("Connection closed unexpectedly while reading SOCKS5 frame.");
            }

            read += n;
        }

        return buffer;
    }

    /// <summary>
    /// Reads a single byte, throwing a <see cref="Socks5ProtocolException"/> on EOF.
    /// </summary>
    public static async Task<byte> ReadByteExactAsync(this Stream stream, CancellationToken cancellationToken)
    {
        var buffer = await stream.ReadExactAsync(1, cancellationToken).ConfigureAwait(false);
        return buffer[0];
    }
}
