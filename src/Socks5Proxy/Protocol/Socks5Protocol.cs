namespace Socks5Proxy.Protocol;

/// <summary>
/// Wire-format constants defined by RFC 1928 (SOCKS5) and RFC 1929
/// (username/password authentication).
/// </summary>
public static class Socks5Protocol
{
    public const byte Version5 = 0x05;

    public const byte AuthNoAuth = 0x00;
    public const byte AuthGssApi = 0x01;
    public const byte AuthUsernamePassword = 0x02;
    public const byte AuthNoAcceptableMethods = 0xFF;

    public const byte AuthUserPassVersion = 0x01;
    public const byte AuthUserPassSuccess = 0x00;
    public const byte AuthUserPassFailure = 0x01;

    public const int MaxDomainNameLength = 255;
    public const int MaxGreetingMethods = 255;
    public const int MaxUdpDatagramSize = 65507;
}
