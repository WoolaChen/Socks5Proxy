using System.Net;
using Socks5Proxy.Protocol;
using Xunit;

namespace Socks5Proxy.Tests;

public class Socks5AddressCodecTests
{
    [Fact]
    public void EncodeReply_UsesIPv4AddressType_ForIPv4Address()
    {
        var reply = Socks5AddressCodec.EncodeReply(Socks5ReplyCode.Succeeded, IPAddress.Parse("10.0.0.5"), 1234);

        Assert.Equal(Socks5Protocol.Version5, reply[0]);
        Assert.Equal((byte)Socks5ReplyCode.Succeeded, reply[1]);
        Assert.Equal(0x00, reply[2]);
        Assert.Equal((byte)Socks5AddressType.IPv4, reply[3]);
        Assert.Equal(new byte[] { 10, 0, 0, 5 }, reply[4..8]);
        Assert.Equal((ushort)1234, (ushort)((reply[8] << 8) | reply[9]));
    }

    [Fact]
    public void EncodeReply_UsesIPv6AddressType_ForIPv6Address()
    {
        var address = IPAddress.Parse("::1");
        var reply = Socks5AddressCodec.EncodeReply(Socks5ReplyCode.Succeeded, address, 80);

        Assert.Equal((byte)Socks5AddressType.IPv6, reply[3]);
        Assert.Equal(22, reply.Length); // 3 header bytes + 1 atyp + 16 addr + 2 port
    }

    [Fact]
    public void TryParseAddress_ParsesIPv4()
    {
        var buffer = new byte[] { 0x01, 127, 0, 0, 1, 0x1F, 0x90 }; // atyp handled externally; offset 1 here
        var ok = Socks5AddressCodec.TryParseAddress(buffer, 1, Socks5AddressType.IPv4, out var host, out var port, out var consumed);

        Assert.True(ok);
        Assert.Equal("127.0.0.1", host);
        Assert.Equal((ushort)8080, port);
        Assert.Equal(6, consumed);
    }

    [Fact]
    public void TryParseAddress_ParsesDomainName()
    {
        var domain = "example.com"u8.ToArray();
        var buffer = new byte[1 + domain.Length + 2];
        buffer[0] = (byte)domain.Length;
        domain.CopyTo(buffer, 1);
        buffer[^2] = 0x00;
        buffer[^1] = 0x50; // port 80

        var ok = Socks5AddressCodec.TryParseAddress(buffer, 0, Socks5AddressType.DomainName, out var host, out var port, out var consumed);

        Assert.True(ok);
        Assert.Equal("example.com", host);
        Assert.Equal((ushort)80, port);
        Assert.Equal(1 + domain.Length + 2, consumed);
    }

    [Fact]
    public void TryParseAddress_ReturnsFalse_WhenBufferTooShort()
    {
        var buffer = new byte[] { 127, 0, 0, 1 }; // missing port bytes
        var ok = Socks5AddressCodec.TryParseAddress(buffer, 0, Socks5AddressType.IPv4, out _, out _, out _);

        Assert.False(ok);
    }
}
