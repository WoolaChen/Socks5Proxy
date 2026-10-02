using System.Security.Cryptography;
using System.Text;
using Socks5Proxy.Configuration;
using Socks5Proxy.Networking;
using Socks5Proxy.Protocol;

namespace Socks5Proxy.Authentication;

/// <summary>
/// Implements the RFC 1929 username/password authentication sub-negotiation.
/// </summary>
public sealed class UsernamePasswordAuthenticator : ISocks5Authenticator
{
    private readonly Dictionary<string, string> _credentials;

    public UsernamePasswordAuthenticator(IEnumerable<Socks5Credential> credentials)
    {
        _credentials = credentials.ToDictionary(c => c.Username, c => c.Password, StringComparer.Ordinal);
    }

    public byte Method => Socks5Protocol.AuthUsernamePassword;

    public async Task<bool> AuthenticateAsync(Stream stream, CancellationToken cancellationToken)
    {
        var version = await stream.ReadByteExactAsync(cancellationToken).ConfigureAwait(false);
        if (version != Socks5Protocol.AuthUserPassVersion)
        {
            throw new Socks5ProtocolException("Unsupported username/password auth sub-negotiation version.");
        }

        var usernameLength = await stream.ReadByteExactAsync(cancellationToken).ConfigureAwait(false);
        var usernameBytes = await stream.ReadExactAsync(usernameLength, cancellationToken).ConfigureAwait(false);
        var username = Encoding.UTF8.GetString(usernameBytes);

        var passwordLength = await stream.ReadByteExactAsync(cancellationToken).ConfigureAwait(false);
        var passwordBytes = await stream.ReadExactAsync(passwordLength, cancellationToken).ConfigureAwait(false);
        var password = Encoding.UTF8.GetString(passwordBytes);

        var ok = _credentials.TryGetValue(username, out var expectedPassword) &&
                 FixedTimeEquals(expectedPassword, password);

        var response = new[]
        {
            Socks5Protocol.AuthUserPassVersion,
            ok ? Socks5Protocol.AuthUserPassSuccess : Socks5Protocol.AuthUserPassFailure
        };
        await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        return ok;
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        // CryptographicOperations.FixedTimeEquals requires equal-length spans;
        // mismatched lengths are compared against a same-length buffer so that
        // timing does not leak the correct credential length.
        if (expectedBytes.Length != actualBytes.Length)
        {
            CryptographicOperations.FixedTimeEquals(actualBytes, actualBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
