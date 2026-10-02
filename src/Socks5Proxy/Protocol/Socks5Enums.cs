namespace Socks5Proxy.Protocol;

public enum Socks5Command : byte
{
    Connect = 0x01,
    Bind = 0x02,
    UdpAssociate = 0x03
}

public enum Socks5AddressType : byte
{
    IPv4 = 0x01,
    DomainName = 0x03,
    IPv6 = 0x04
}

public enum Socks5ReplyCode : byte
{
    Succeeded = 0x00,
    GeneralFailure = 0x01,
    ConnectionNotAllowed = 0x02,
    NetworkUnreachable = 0x03,
    HostUnreachable = 0x04,
    ConnectionRefused = 0x05,
    TtlExpired = 0x06,
    CommandNotSupported = 0x07,
    AddressTypeNotSupported = 0x08
}
