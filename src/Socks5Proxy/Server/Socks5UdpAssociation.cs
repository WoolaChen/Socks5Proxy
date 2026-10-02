using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Socks5Proxy.Configuration;
using Socks5Proxy.Protocol;

namespace Socks5Proxy.Server;

/// <summary>
/// Manages a single UDP ASSOCIATE session: one dedicated UDP socket, state
/// tracking of the learned client endpoint and recently used target
/// endpoints, and an idle timeout independent of the TCP control connection.
/// </summary>
public sealed class Socks5UdpAssociation : IAsyncDisposable
{
    private readonly Socket _udpSocket;
    private readonly Socks5ServerOptions _options;
    private readonly ILogger _logger;
    private readonly IPAddress _expectedClientAddress;
    private readonly ConcurrentDictionary<IPEndPoint, DateTime> _activeTargets = new();
    private IPEndPoint? _clientEndPoint;
    private long _lastActivityTicks = Environment.TickCount64;

    private Socks5UdpAssociation(Socket udpSocket, IPAddress expectedClientAddress, Socks5ServerOptions options, ILogger logger)
    {
        _udpSocket = udpSocket;
        _expectedClientAddress = expectedClientAddress;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Binds a fresh UDP socket for this association. When IPv6 is available the
    /// socket is bound in dual-mode so the relay can reach and be reached by both
    /// IPv4 and IPv6 endpoints regardless of the control connection's address family.
    /// </summary>
    public static Socks5UdpAssociation Create(IPAddress expectedClientAddress, AddressFamily family, Socks5ServerOptions options, ILogger logger)
    {
        try
        {
            var dualModeSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp)
            {
                DualMode = true
            };
            dualModeSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
            return new Socks5UdpAssociation(dualModeSocket, expectedClientAddress, options, logger);
        }
        catch (SocketException ex)
        {
            logger.LogDebug(ex, "IPv6 dual-mode UDP socket unavailable; falling back to IPv4-only relay socket.");
        }

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, 0));
        return new Socks5UdpAssociation(socket, expectedClientAddress, options, logger);
    }

    public IPEndPoint LocalEndPoint => (IPEndPoint)_udpSocket.LocalEndPoint!;

    /// <summary>
    /// A dual-mode IPv6 socket always reports remote IPv4 peers using their
    /// IPv4-mapped-to-IPv6 form. Normalizing every endpoint we store or compare
    /// to that same form keeps dictionary lookups and equality checks consistent
    /// regardless of whether the original address was IPv4 or IPv6.
    /// </summary>
    private IPEndPoint Normalize(IPEndPoint endPoint)
    {
        if (_udpSocket.AddressFamily == AddressFamily.InterNetworkV6 && endPoint.AddressFamily == AddressFamily.InterNetwork)
        {
            return new IPEndPoint(endPoint.Address.MapToIPv6(), endPoint.Port);
        }

        return endPoint;
    }

    /// <summary>
    /// Runs the relay loop until cancelled or idle for longer than the configured
    /// UDP idle timeout. Designed to be run alongside a control-connection watcher.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var idleCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idleCts.Token);
        using var idleTimer = new Timer(_ =>
        {
            var elapsedMs = Environment.TickCount64 - Interlocked.Read(ref _lastActivityTicks);
            if (elapsedMs >= _options.UdpIdleTimeout.TotalMilliseconds)
            {
                idleCts.Cancel();
            }
        }, null, _options.UdpIdleTimeout, TimeSpan.FromSeconds(5));

        var buffer = new byte[Socks5Protocol.MaxUdpDatagramSize];
        var receiveBuffer = new ArraySegment<byte>(buffer);

        try
        {
            while (!linkedCts.IsCancellationRequested)
            {
                SocketReceiveFromResult result;
                try
                {
                    result = await _udpSocket.ReceiveFromAsync(receiveBuffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), linkedCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogDebug(ex, "UDP association socket error; terminating relay loop.");
                    break;
                }

                Interlocked.Exchange(ref _lastActivityTicks, Environment.TickCount64);
                var sender = (IPEndPoint)result.RemoteEndPoint;

                if (IsFromClient(sender))
                {
                    _clientEndPoint ??= sender;
                    await HandleClientDatagramAsync(buffer.AsMemory(0, result.ReceivedBytes), linkedCts.Token).ConfigureAwait(false);
                }
                else if (_activeTargets.ContainsKey(sender))
                {
                    await HandleTargetReplyAsync(sender, buffer.AsMemory(0, result.ReceivedBytes), linkedCts.Token).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("Dropping unsolicited UDP datagram from {Sender}.", sender);
                }
            }
        }
        finally
        {
            _logger.LogDebug("UDP association on {Local} terminated.", LocalEndPoint);
        }
    }

    private bool IsFromClient(IPEndPoint sender)
    {
        if (_clientEndPoint is not null)
        {
            return sender.Equals(_clientEndPoint);
        }

        // Before the client's first datagram is observed, accept only packets
        // whose source IP matches the address used for the TCP control connection.
        var expected = _udpSocket.AddressFamily == AddressFamily.InterNetworkV6 && _expectedClientAddress.AddressFamily == AddressFamily.InterNetwork
            ? _expectedClientAddress.MapToIPv6()
            : _expectedClientAddress;

        return sender.Address.Equals(expected);
    }

    private async Task HandleClientDatagramAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken)
    {
        if (!TryParseClientDatagram(datagram, out var host, out var port, out var payloadOffset))
        {
            return;
        }

        var payload = datagram.Slice(payloadOffset);

        IPAddress targetAddress;
        if (!IPAddress.TryParse(host, out targetAddress!))
        {
            var resolved = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            if (resolved.Length == 0)
            {
                _logger.LogDebug("Could not resolve UDP target host '{Host}'.", host);
                return;
            }

            targetAddress = resolved[0];
        }

        var targetEndPoint = Normalize(new IPEndPoint(targetAddress, port));
        _activeTargets[targetEndPoint] = DateTime.UtcNow;

        try
        {
            await _udpSocket.SendToAsync(payload.ToArray(), SocketFlags.None, targetEndPoint, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Failed to forward UDP datagram to {Target}.", targetEndPoint);
        }
    }

    // Kept synchronous (no awaits) so that the Span<byte> derived from the
    // datagram never needs to be preserved across an async suspension point.
    private bool TryParseClientDatagram(ReadOnlyMemory<byte> datagram, out string host, out ushort port, out int payloadOffset)
    {
        host = string.Empty;
        port = 0;
        payloadOffset = 0;

        var span = datagram.Span;
        if (span.Length < 4)
        {
            return false;
        }

        if (span[0] != 0x00 || span[1] != 0x00)
        {
            _logger.LogDebug("Dropping malformed UDP datagram (bad reserved bytes).");
            return false;
        }

        byte fragment = span[2];
        if (fragment != 0x00)
        {
            _logger.LogDebug("Dropping fragmented UDP datagram (fragmentation is not supported).");
            return false;
        }

        var addressType = (Socks5AddressType)span[3];
        if (!Socks5AddressCodec.TryParseAddress(span, 4, addressType, out host, out port, out var consumed))
        {
            _logger.LogDebug("Dropping UDP datagram with invalid address encoding.");
            return false;
        }

        payloadOffset = 4 + consumed;
        return true;
    }

    private async Task HandleTargetReplyAsync(IPEndPoint target, ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken)
    {
        if (_clientEndPoint is null)
        {
            return;
        }

        _activeTargets[target] = DateTime.UtcNow;

        var replyAddress = target.Address.IsIPv4MappedToIPv6 ? target.Address.MapToIPv4() : target.Address;
        var header = Socks5AddressCodec.EncodeAddress(replyAddress, (ushort)target.Port);
        var packet = new byte[3 + header.Length + datagram.Length];
        packet[0] = 0x00;
        packet[1] = 0x00;
        packet[2] = 0x00; // FRAG
        header.CopyTo(packet, 3);
        datagram.Span.CopyTo(packet.AsSpan(3 + header.Length));

        try
        {
            await _udpSocket.SendToAsync(packet, SocketFlags.None, _clientEndPoint, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            _logger.LogDebug(ex, "Failed to deliver UDP reply from {Target} back to client.", target);
        }
    }

    public ValueTask DisposeAsync()
    {
        _udpSocket.Dispose();
        return ValueTask.CompletedTask;
    }
}
