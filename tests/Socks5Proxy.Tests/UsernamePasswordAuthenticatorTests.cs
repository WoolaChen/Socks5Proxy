using Socks5Proxy.Authentication;
using Socks5Proxy.Configuration;
using Xunit;

namespace Socks5Proxy.Tests;

public class UsernamePasswordAuthenticatorTests
{
    [Fact]
    public async Task AuthenticateAsync_Succeeds_WithCorrectCredentials()
    {
        var (clientStream, serverStream, clientSocket, serverSocket) = await LoopbackStreamPair.CreateAsync();
        using var _ = clientSocket;
        using var __ = serverSocket;

        var authenticator = new UsernamePasswordAuthenticator(new[] { new Socks5Credential { Username = "alice", Password = "secret" } });

        var serverTask = authenticator.AuthenticateAsync(serverStream, CancellationToken.None);

        await WriteAuthRequestAsync(clientStream, "alice", "secret");
        var clientResponse = await ReadExactAsync(clientStream, 2);

        var serverResult = await serverTask;

        Assert.True(serverResult);
        Assert.Equal(0x01, clientResponse[0]);
        Assert.Equal(0x00, clientResponse[1]); // success
    }

    [Fact]
    public async Task AuthenticateAsync_Fails_WithIncorrectPassword()
    {
        var (clientStream, serverStream, clientSocket, serverSocket) = await LoopbackStreamPair.CreateAsync();
        using var _ = clientSocket;
        using var __ = serverSocket;

        var authenticator = new UsernamePasswordAuthenticator(new[] { new Socks5Credential { Username = "alice", Password = "secret" } });

        var serverTask = authenticator.AuthenticateAsync(serverStream, CancellationToken.None);

        await WriteAuthRequestAsync(clientStream, "alice", "wrong-password");
        var clientResponse = await ReadExactAsync(clientStream, 2);

        var serverResult = await serverTask;

        Assert.False(serverResult);
        Assert.Equal(0x01, clientResponse[1]); // failure
    }

    [Fact]
    public async Task AuthenticateAsync_Fails_WithUnknownUsername()
    {
        var (clientStream, serverStream, clientSocket, serverSocket) = await LoopbackStreamPair.CreateAsync();
        using var _ = clientSocket;
        using var __ = serverSocket;

        var authenticator = new UsernamePasswordAuthenticator(new[] { new Socks5Credential { Username = "alice", Password = "secret" } });

        var serverTask = authenticator.AuthenticateAsync(serverStream, CancellationToken.None);

        await WriteAuthRequestAsync(clientStream, "bob", "secret");
        await ReadExactAsync(clientStream, 2);

        var serverResult = await serverTask;

        Assert.False(serverResult);
    }

    private static async Task WriteAuthRequestAsync(Stream stream, string username, string password)
    {
        var usernameBytes = System.Text.Encoding.UTF8.GetBytes(username);
        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);

        using var buffer = new MemoryStream();
        buffer.WriteByte(0x01);
        buffer.WriteByte((byte)usernameBytes.Length);
        buffer.Write(usernameBytes);
        buffer.WriteByte((byte)passwordBytes.Length);
        buffer.Write(passwordBytes);

        await stream.WriteAsync(buffer.ToArray());
        await stream.FlushAsync();
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
}
