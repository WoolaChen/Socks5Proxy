using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Socks5Proxy.Protocol;

/// <summary>
/// Encodes and decodes the SOCKS5 address/port wire representation shared by
/// request/reply messages on the control connection and by UDP datagram headers.
/// </summary>
public static class Socks5AddressCodec
{
    /// <summary>
    /// Builds the ATYP + address + port bytes used in replies and UDP headers.
    /// </summary>
    public static byte[] EncodeAddress(IPAddress address, ushort port)
    {
        var atyp = address.AddressFamily == AddressFamily.InterNetworkV6
            ? Socks5AddressType.IPv6
            : Socks5AddressType.IPv4;

        var addressBytes = address.GetAddressBytes();
        var buffer = new byte[1 + addressBytes.Length + 2];
        buffer[0] = (byte)atyp;
        addressBytes.CopyTo(buffer, 1);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(1 + addressBytes.Length, 2), port);
        return buffer;
    }

    /// <summary>
    /// Builds a complete SOCKS5 reply message: VER REP RSV ATYP ADDR PORT.
    /// </summary>
    public static byte[] EncodeReply(Socks5ReplyCode reply, IPAddress bindAddress, ushort bindPort)
    {
        var addressPart = EncodeAddress(bindAddress, bindPort);
        var buffer = new byte[3 + addressPart.Length];
        buffer[0] = Socks5Protocol.Version5;
        buffer[1] = (byte)reply;
        buffer[2] = 0x00;
        addressPart.CopyTo(buffer, 3);
        return buffer;
    }

    /// <summary>
    /// Parses an address + port from a UDP datagram (or any in-memory buffer)
    /// starting at <paramref name="offset"/>. Returns the number of bytes consumed.
    /// </summary>
    public static bool TryParseAddress(
        ReadOnlySpan<byte> buffer,
        int offset,
        Socks5AddressType addressType,
        out string host,
        out ushort port,
        out int bytesConsumed)
    {
        host = string.Empty;
        port = 0;
        bytesConsumed = 0;

        switch (addressType)
        {
            case Socks5AddressType.IPv4:
                if (buffer.Length < offset + 4 + 2) return false;
                host = new IPAddress(buffer.Slice(offset, 4)).ToString();
                port = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset + 4, 2));
                bytesConsumed = 4 + 2;
                return true;

            case Socks5AddressType.IPv6:
                if (buffer.Length < offset + 16 + 2) return false;
                host = new IPAddress(buffer.Slice(offset, 16)).ToString();
                port = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset + 16, 2));
                bytesConsumed = 16 + 2;
                return true;

            case Socks5AddressType.DomainName:
                if (buffer.Length < offset + 1) return false;
                int len = buffer[offset];
                if (buffer.Length < offset + 1 + len + 2) return false;
                host = Encoding.UTF8.GetString(buffer.Slice(offset + 1, len));
                port = BinaryPrimitives.ReadUInt16BigEndian(buffer.Slice(offset + 1 + len, 2));
                bytesConsumed = 1 + len + 2;
                return true;

            default:
                return false;
        }
    }

    public static IPEndPoint AnyEndPointFor(AddressFamily family) =>
        new(family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
}
