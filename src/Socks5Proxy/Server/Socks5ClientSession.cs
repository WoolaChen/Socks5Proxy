using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Socks5Proxy.Authentication;
using Socks5Proxy.Configuration;
using Socks5Proxy.Networking;
using Socks5Proxy.Protocol;

namespace Socks5Proxy.Server;

/// <summary>
/// Handles the full lifecycle of a single accepted client connection:
/// greeting, authentication, command dispatch and relay/cleanup.
/// </summary>
public sealed class Socks5ClientSession : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly Socks5ServerOptions _options;
    private readonly IReadOnlyDictionary<byte, ISocks5Authenticator> _authenticators;
    private readonly ILogger _logger;
    private Stream _stream;

    public Socks5ClientSession(
        TcpClient client,
        Stream stream,
        Socks5ServerOptions options,
        IReadOnlyDictionary<byte, ISocks5Authenticator> authenticators,
        ILogger logger)
    {
        _client = client;
        _stream = stream;
        _options = options;
        _authenticators = authenticators;
        _logger = logger;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var remoteEndPoint = (IPEndPoint)_client.Client.RemoteEndPoint!;

        try
        {
            await TimeoutHelper.WithTimeoutAsync(
                ct => NegotiateAsync(ct),
                _options.HandshakeTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Handshake with {RemoteEndPoint} timed out.", remoteEndPoint);
            return;
        }
        catch (Socks5ProtocolException ex)
        {
            _logger.LogWarning("Handshake with {RemoteEndPoint} failed: {Message}", remoteEndPoint, ex.Message);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Authentication failed for {RemoteEndPoint}.", remoteEndPoint);
            return;
        }

        Socks5Request request;
        try
        {
            request = await ReadRequestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Socks5ProtocolException ex)
        {
            _logger.LogWarning(ex, "Invalid request from {RemoteEndPoint}.", remoteEndPoint);
            await TrySendReplyAsync(Socks5ReplyCode.GeneralFailure, cancellationToken).ConfigureAwait(false);
            return;
        }

        switch (request.Command)
        {
            case Socks5Command.Connect:
                await HandleConnectAsync(request, remoteEndPoint, cancellationToken).ConfigureAwait(false);
                break;

            case Socks5Command.UdpAssociate when _options.EnableUdpAssociate:
                await HandleUdpAssociateAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);
                break;

            default:
                _logger.LogInformation("Rejecting unsupported command {Command} from {RemoteEndPoint}.", request.Command, remoteEndPoint);
                await TrySendReplyAsync(Socks5ReplyCode.CommandNotSupported, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task NegotiateAsync(CancellationToken cancellationToken)
    {
        var header = await _stream.ReadExactAsync(2, cancellationToken).ConfigureAwait(false);
        if (header[0] != Socks5Protocol.Version5)
        {
            throw new Socks5ProtocolException($"Unsupported SOCKS version 0x{header[0]:X2}.");
        }

        int methodCount = header[1];
        var methods = methodCount == 0
            ? Array.Empty<byte>()
            : await _stream.ReadExactAsync(methodCount, cancellationToken).ConfigureAwait(false);

        var selected = methods.FirstOrDefault(m => _authenticators.ContainsKey(m), Socks5Protocol.AuthNoAcceptableMethods);

        await _stream.WriteAsync(new[] { Socks5Protocol.Version5, selected }, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        if (selected == Socks5Protocol.AuthNoAcceptableMethods)
        {
            throw new Socks5ProtocolException("No acceptable authentication method offered by client.");
        }

        var authenticator = _authenticators[selected];
        var ok = await authenticator.AuthenticateAsync(_stream, cancellationToken).ConfigureAwait(false);
        if (!ok)
        {
            throw new UnauthorizedAccessException("Authentication rejected.");
        }
    }

    private async Task<Socks5Request> ReadRequestAsync(CancellationToken cancellationToken)
    {
        var header = await _stream.ReadExactAsync(4, cancellationToken).ConfigureAwait(false);
        if (header[0] != Socks5Protocol.Version5)
        {
            throw new Socks5ProtocolException($"Unsupported SOCKS version 0x{header[0]:X2} in request.");
        }

        if (!Enum.IsDefined(typeof(Socks5Command), header[1]))
        {
            throw new Socks5ProtocolException($"Unknown command 0x{header[1]:X2}.", Socks5ReplyCode.CommandNotSupported);
        }

        var command = (Socks5Command)header[1];
        var addressType = (Socks5AddressType)header[3];

        string host;
        switch (addressType)
        {
            case Socks5AddressType.IPv4:
                host = new IPAddress(await _stream.ReadExactAsync(4, cancellationToken).ConfigureAwait(false)).ToString();
                break;

            case Socks5AddressType.IPv6:
                host = new IPAddress(await _stream.ReadExactAsync(16, cancellationToken).ConfigureAwait(false)).ToString();
                break;

            case Socks5AddressType.DomainName:
                var length = await _stream.ReadByteExactAsync(cancellationToken).ConfigureAwait(false);
                var domainBytes = await _stream.ReadExactAsync(length, cancellationToken).ConfigureAwait(false);
                host = Encoding.UTF8.GetString(domainBytes);
                break;

            default:
                throw new Socks5ProtocolException($"Unsupported address type 0x{header[3]:X2}.", Socks5ReplyCode.AddressTypeNotSupported);
        }

        var portBytes = await _stream.ReadExactAsync(2, cancellationToken).ConfigureAwait(false);
        var port = BinaryPrimitives.ReadUInt16BigEndian(portBytes);

        return new Socks5Request(command, addressType, host, port);
    }

    private async Task HandleConnectAsync(Socks5Request request, IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        TcpClient? upstream = null;
        try
        {
            var addresses = await request.ResolveAsync(cancellationToken).ConfigureAwait(false);

            Exception? lastError = null;
            foreach (var address in addresses)
            {
                upstream = new TcpClient(address.AddressFamily);
                try
                {
                    await TimeoutHelper.WithTimeoutAsync(
                        ct => upstream.ConnectAsync(address, request.Port, ct).AsTask(),
                        _options.ConnectTimeout,
                        cancellationToken).ConfigureAwait(false);
                    lastError = null;
                    break;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    upstream.Dispose();
                    upstream = null;
                }
            }

            if (upstream is null)
            {
                throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
            }

            var localEndPoint = (IPEndPoint)upstream.Client.LocalEndPoint!;
            await SendReplyAsync(Socks5ReplyCode.Succeeded, localEndPoint.Address, (ushort)localEndPoint.Port, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("CONNECT {Remote} -> {Host}:{Port} established.", remoteEndPoint, request.Host, request.Port);

            using var upstreamStream = upstream.GetStream();
            await Socks5Relay.RelayAsync(_stream, upstreamStream, _options.IdleTimeout, _logger, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("CONNECT {Remote} -> {Host}:{Port} timed out.", remoteEndPoint, request.Host, request.Port);
            await TrySendReplyAsync(Socks5ReplyCode.TtlExpired, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning("CONNECT {Remote} -> {Host}:{Port} failed: {Message}", remoteEndPoint, request.Host, request.Port, ex.Message);
            await TrySendReplyAsync(MapSocketError(ex.SocketErrorCode), cancellationToken).ConfigureAwait(false);
        }
        catch (Socks5ProtocolException ex)
        {
            _logger.LogWarning("CONNECT {Remote} -> {Host}:{Port} rejected: {Message}", remoteEndPoint, request.Host, request.Port, ex.Message);
            await TrySendReplyAsync(ex.ReplyCode, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error handling CONNECT from {Remote}.", remoteEndPoint);
            await TrySendReplyAsync(Socks5ReplyCode.GeneralFailure, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            upstream?.Dispose();
        }
    }

    private async Task HandleUdpAssociateAsync(IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
    {
        await using var association = Socks5UdpAssociation.Create(
            remoteEndPoint.Address,
            ((IPEndPoint)_client.Client.LocalEndPoint!).AddressFamily,
            _options,
            _logger);

        await SendReplyAsync(Socks5ReplyCode.Succeeded, association.LocalEndPoint.Address, (ushort)association.LocalEndPoint.Port, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation("UDP ASSOCIATE for {Remote} bound relay socket on {Local}.", remoteEndPoint, association.LocalEndPoint);

        using var controlClosedCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, controlClosedCts.Token);

        var watchControlConnection = WatchControlConnectionAsync(controlClosedCts, cancellationToken);
        var relayLoop = association.RunAsync(linkedCts.Token);

        await Task.WhenAny(watchControlConnection, relayLoop).ConfigureAwait(false);
        controlClosedCts.Cancel();

        try
        {
            await Task.WhenAll(watchControlConnection, relayLoop).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    private async Task WatchControlConnectionAsync(CancellationTokenSource controlClosedCts, CancellationToken cancellationToken)
    {
        try
        {
            var probe = new byte[1];
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(probe.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (IOException)
        {
            // Control connection dropped.
        }
        finally
        {
            controlClosedCts.Cancel();
        }
    }

    private static Socks5ReplyCode MapSocketError(SocketError error) => error switch
    {
        SocketError.ConnectionRefused => Socks5ReplyCode.ConnectionRefused,
        SocketError.HostUnreachable => Socks5ReplyCode.HostUnreachable,
        SocketError.HostNotFound => Socks5ReplyCode.HostUnreachable,
        SocketError.NetworkUnreachable => Socks5ReplyCode.NetworkUnreachable,
        SocketError.TimedOut => Socks5ReplyCode.TtlExpired,
        SocketError.AccessDenied => Socks5ReplyCode.ConnectionNotAllowed,
        _ => Socks5ReplyCode.GeneralFailure
    };

    private async Task SendReplyAsync(Socks5ReplyCode reply, IPAddress bindAddress, ushort bindPort, CancellationToken cancellationToken)
    {
        var message = Socks5AddressCodec.EncodeReply(reply, bindAddress, bindPort);
        await _stream.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task TrySendReplyAsync(Socks5ReplyCode reply, CancellationToken cancellationToken)
    {
        try
        {
            await SendReplyAsync(reply, IPAddress.Any, 0, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort only; the connection may already be broken.
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _client.Dispose();
        }
    }
}
