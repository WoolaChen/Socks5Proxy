namespace Socks5Proxy.Protocol;

/// <summary>
/// Thrown when a peer sends data that violates the SOCKS5 wire protocol.
/// These are treated as client errors and are never allowed to crash the server.
/// </summary>
public sealed class Socks5ProtocolException : Exception
{
    public Socks5ReplyCode ReplyCode { get; }

    public Socks5ProtocolException(string message, Socks5ReplyCode replyCode = Socks5ReplyCode.GeneralFailure)
        : base(message)
    {
        ReplyCode = replyCode;
    }

    public Socks5ProtocolException(string message, Exception innerException, Socks5ReplyCode replyCode = Socks5ReplyCode.GeneralFailure)
        : base(message, innerException)
    {
        ReplyCode = replyCode;
    }
}
