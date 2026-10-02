namespace Socks5Proxy.Authentication;

/// <summary>
/// Negotiates and verifies a single SOCKS5 authentication method.
/// </summary>
public interface ISocks5Authenticator
{
    /// <summary>
    /// The SOCKS5 authentication method identifier this authenticator handles.
    /// </summary>
    byte Method { get; }

    /// <summary>
    /// Performs the authentication sub-negotiation on the given stream.
    /// Returns true when authentication succeeded.
    /// </summary>
    Task<bool> AuthenticateAsync(Stream stream, CancellationToken cancellationToken);
}
