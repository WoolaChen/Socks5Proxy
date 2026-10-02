using System.Net;

namespace Socks5Proxy.Protocol;

/// <summary>
/// A parsed SOCKS5 request (CONNECT / BIND / UDP ASSOCIATE) as received on the
/// control connection after a successful handshake.
/// </summary>
/// <param name="Command">The requested SOCKS5 command.</param>
/// <param name="AddressType">The address type used to encode the destination.</param>
/// <param name="Host">The destination hostname or textual IP address.</param>
/// <param name="Port">The destination port.</param>
public sealed record Socks5Request(Socks5Command Command, Socks5AddressType AddressType, string Host, ushort Port)
{
    /// <summary>
    /// Resolves <see cref="Host"/> to an <see cref="IPAddress"/> when it is
    /// already a literal IPv4/IPv6 address, otherwise performs DNS resolution.
    /// </summary>
    public async Task<IPAddress[]> ResolveAsync(CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(Host, out var literal))
        {
            return new[] { literal };
        }

        var entries = await Dns.GetHostAddressesAsync(Host, cancellationToken).ConfigureAwait(false);
        if (entries.Length == 0)
        {
            throw new Socks5ProtocolException($"Could not resolve host '{Host}'.", Socks5ReplyCode.HostUnreachable);
        }

        return entries;
    }
}
