using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Socks5Proxy.Configuration;
using Socks5Proxy.Server;
using Xunit;

namespace Socks5Proxy.Tests;

public class Socks5ServerIntegrationTests : IAsyncLifetime
{
    private Socks5Server? _server;
    private CancellationTokenSource? _cts;
    private int _port;

    public async Task InitializeAsync()
    {
        _port = GetFreeTcpPort();
        var options = new Socks5ServerOptions
        {
            ListenAddress = IPAddress.Loopback.ToString(),
            ListenPort = _port,
            DualMode = false,
            EnableNoAuth = true,
            MaxConcurrentConnections = 2,
            HandshakeTimeoutSeconds = 2,
            ConnectTimeoutSeconds = 2,
            IdleTimeoutSeconds = 5,
            EnableUdpAssociate = true
        };

        _server = new Socks5Server(options, NullLoggerFactory.Instance);
        _cts = new CancellationTokenSource();
        _ = _server.RunAsync(_cts.Token);

        await WaitForListenerAsync();
    }

    public async Task DisposeAsync()
    {
        _cts?.Cancel();
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }
    }

    [Fact]
    public async Task Connect_RelaysDataToUpstreamServer_AndBack()
    {
        using var echoListener = new TcpListener(IPAddress.Loopback, 0);
        echoListener.Start();
        var echoPort = ((IPEndPoint)echoListener.LocalEndpoint).Port;
        var echoTask = Task.Run(async () =>
        {
            using var upstream = await echoListener.AcceptTcpClientAsync();
            using var stream = upstream.GetStream();
            var buffer = new byte[1024];
            var read = await stream.ReadAsync(buffer);
            await stream.WriteAsync(buffer.AsMemory(0, read));
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        using var stream = client.GetStream();

        await stream.WriteAsync(new byte[] { 5, 1, 0 });
        var greeting = await ReadExactAsync(stream, 2);
        Assert.Equal(new byte[] { 5, 0 }, greeting);

        var request = BuildConnectRequest(IPAddress.Loopback, (ushort)echoPort);
        await stream.WriteAsync(request);
        var reply = await ReadConnectReplyAsync(stream);
        Assert.Equal(0x00, reply[1]);

        var payload = Encoding.UTF8.GetBytes("hello");
        await stream.WriteAsync(payload);

        var response = new byte[payload.Length];
        var totalRead = 0;
        while (totalRead < response.Length)
        {
            var n = await stream.ReadAsync(response.AsMemory(totalRead));
            if (n == 0) break;
            totalRead += n;
        }

        Assert.Equal("hello", Encoding.UTF8.GetString(response, 0, totalRead));
        await echoTask;
    }

    [Fact]
    public async Task Connect_IsRejected_WhenConnectionLimitExceeded()
    {
        using var clientA = new TcpClient();
        await clientA.ConnectAsync(IPAddress.Loopback, _port);
        await clientA.GetStream().WriteAsync(new byte[] { 5, 1, 0 });
        await ReadExactAsync(clientA.GetStream(), 2);

        using var clientB = new TcpClient();
        await clientB.ConnectAsync(IPAddress.Loopback, _port);
        await clientB.GetStream().WriteAsync(new byte[] { 5, 1, 0 });
        await ReadExactAsync(clientB.GetStream(), 2);

        using var clientC = new TcpClient();
        await clientC.ConnectAsync(IPAddress.Loopback, _port);
        var buffer = new byte[2];
        var read = await clientC.GetStream().ReadAsync(buffer);

        Assert.Equal(0, read); // connection closed immediately due to connection limit
    }

    [Fact]
    public async Task Handshake_TimesOut_WhenClientSendsNothing()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);

        var buffer = new byte[1];
        var read = await client.GetStream().ReadAsync(buffer);

        Assert.Equal(0, read); // server closed the idle connection after the handshake timeout
    }

    private static byte[] BuildConnectRequest(IPAddress address, ushort port)
    {
        var addressBytes = address.GetAddressBytes();
        var buffer = new byte[4 + addressBytes.Length + 2];
        buffer[0] = 5;
        buffer[1] = 1; // CONNECT
        buffer[2] = 0;
        buffer[3] = 1; // IPv4
        addressBytes.CopyTo(buffer, 4);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4 + addressBytes.Length, 2), port);
        return buffer;
    }

    private static async Task<byte[]> ReadConnectReplyAsync(Stream stream)
    {
        var header = await ReadExactAsync(stream, 4);
        var addressLength = header[3] switch
        {
            1 => 4,
            4 => 16,
            _ => throw new InvalidOperationException("Unexpected address type in reply.")
        };
        var rest = await ReadExactAsync(stream, addressLength + 2);
        return header.Concat(rest).ToArray();
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, length - read));
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }

        return buffer;
    }

    private async Task WaitForListenerAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, _port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(50);
            }
        }

        throw new TimeoutException("Server did not start listening in time.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
