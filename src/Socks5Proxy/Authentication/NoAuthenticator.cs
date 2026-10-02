using Socks5Proxy.Protocol;

namespace Socks5Proxy.Authentication;

/// <summary>
/// Implements the SOCKS5 "no authentication required" method (0x00).
/// </summary>
public sealed class NoAuthenticator : ISocks5Authenticator
{
    public byte Method => Socks5Protocol.AuthNoAuth;

    public Task<bool> AuthenticateAsync(Stream stream, CancellationToken cancellationToken) => Task.FromResult(true);
}
