using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Socks5Proxy.Authentication;
using Socks5Proxy.Configuration;
using Socks5Proxy.Protocol;

namespace Socks5Proxy.Server;

/// <summary>
/// Listens for SOCKS5 client connections and dispatches each to a
/// <see cref="Socks5ClientSession"/>, enforcing a global concurrent connection limit.
/// </summary>
public sealed class Socks5Server : IAsyncDisposable
{
    private readonly Socks5ServerOptions _options;
    private readonly ILogger<Socks5Server> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IReadOnlyDictionary<byte, ISocks5Authenticator> _authenticators;
    private readonly SemaphoreSlim _connectionLimiter;
    private readonly Socks5TlsContext _tlsContext;
    private TcpListener? _listener;

    public Socks5Server(Socks5ServerOptions options, ILoggerFactory loggerFactory)
    {
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<Socks5Server>();
        _authenticators = BuildAuthenticators(options);
        _connectionLimiter = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentConnections));
        _tlsContext = Socks5TlsContext.Create(options.Tls, loggerFactory.CreateLogger<Socks5TlsContext>());
    }

    private static IReadOnlyDictionary<byte, ISocks5Authenticator> BuildAuthenticators(Socks5ServerOptions options)
    {
        var map = new Dictionary<byte, ISocks5Authenticator>();

        if (options.EnableNoAuth)
        {
            var noAuth = new NoAuthenticator();
            map[noAuth.Method] = noAuth;
        }

        if (options.EnableUsernamePasswordAuth)
        {
            var userPass = new UsernamePasswordAuthenticator(options.Credentials);
            map[userPass.Method] = userPass;
        }

        if (map.Count == 0)
        {
            throw new InvalidOperationException("At least one authentication method must be enabled.");
        }

        return map;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var address = IPAddress.Parse(_options.ListenAddress);
        _listener = new TcpListener(address, _options.ListenPort);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            _listener.Server.DualMode = _options.DualMode;
        }

        _listener.Start();
        _logger.LogInformation(
            "SOCKS5 proxy listening on {Address}:{Port} (TLS={Tls}, MaxConnections={MaxConnections}).",
            address, _options.ListenPort, _tlsContext.Enabled, _options.MaxConcurrentConnections);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    _logger.LogError(ex, "Error accepting a connection; continuing to listen.");
                    continue;
                }

                _ = Task.Run(() => AcceptClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            _listener.Stop();
        }
    }

    private async Task AcceptClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var remoteEndPoint = client.Client.RemoteEndPoint;

        if (!await _connectionLimiter.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("Connection limit reached ({Limit}); rejecting {RemoteEndPoint}.", _options.MaxConcurrentConnections, remoteEndPoint);
            client.Dispose();
            return;
        }

        try
        {
            client.NoDelay = true;
            Stream stream = client.GetStream();

            try
            {
                stream = await _tlsContext.WrapAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TLS handshake failed for {RemoteEndPoint}.", remoteEndPoint);
                client.Dispose();
                return;
            }

            await using var session = new Socks5ClientSession(
                client,
                stream,
                _options,
                _authenticators,
                _loggerFactory.CreateLogger<Socks5ClientSession>());

            await session.ProcessAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Socks5ProtocolException ex)
        {
            _logger.LogDebug(ex, "Protocol error from {RemoteEndPoint}.", remoteEndPoint);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Connection from {RemoteEndPoint} ended.", remoteEndPoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while servicing {RemoteEndPoint}.", remoteEndPoint);
        }
        finally
        {
            _connectionLimiter.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _listener?.Stop();
        _connectionLimiter.Dispose();
        return ValueTask.CompletedTask;
    }
}
