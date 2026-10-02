using System.Net;
using System.Net.Sockets;

namespace Socks5Proxy.Tests;

/// <summary>
/// Creates a connected pair of local TCP sockets so tests can exercise
/// stream-based protocol code without needing a real remote peer.
/// </summary>
internal static class LoopbackStreamPair
{
    public static async Task<(NetworkStream Client, NetworkStream Server, TcpClient ClientSocket, TcpClient ServerSocket)> CreateAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var acceptTask = listener.AcceptTcpClientAsync();
            var clientSocket = await ConnectClientAsync(port);
            var serverSocket = await acceptTask;

            return (clientSocket.GetStream(), serverSocket.GetStream(), clientSocket, serverSocket);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<TcpClient> ConnectClientAsync(int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        return client;
    }
}
