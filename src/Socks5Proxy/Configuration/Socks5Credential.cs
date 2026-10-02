namespace Socks5Proxy.Configuration;

/// <summary>
/// A single username/password credential accepted by the SOCKS5 authenticator.
/// </summary>
public sealed class Socks5Credential
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
