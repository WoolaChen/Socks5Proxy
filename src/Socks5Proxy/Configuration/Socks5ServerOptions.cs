namespace Socks5Proxy.Configuration;

/// <summary>
/// Strongly typed, production-oriented configuration for the SOCKS5 server.
/// Bound from the "Socks5" configuration section.
/// </summary>
public sealed class Socks5ServerOptions
{
    public const string SectionName = "Socks5";

    /// <summary>
    /// Address to bind the listening socket to. Use "::" to listen on all
    /// IPv6 (and, when <see cref="DualMode"/> is true, IPv4-mapped) addresses.
    /// </summary>
    public string ListenAddress { get; set; } = "::";

    public int ListenPort { get; set; } = 1080;

    /// <summary>
    /// When true and <see cref="ListenAddress"/> is an IPv6 address, the
    /// listening socket accepts both IPv4 and IPv6 connections.
    /// </summary>
    public bool DualMode { get; set; } = true;

    public bool EnableNoAuth { get; set; } = true;

    public bool EnableUsernamePasswordAuth { get; set; }

    public List<Socks5Credential> Credentials { get; set; } = new();

    /// <summary>
    /// Maximum number of concurrently active client sessions. Additional
    /// connections are accepted and immediately closed once the limit is reached.
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 512;

    /// <summary>
    /// Maximum time allowed to complete the greeting/authentication handshake.
    /// </summary>
    public int HandshakeTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum time allowed for an outbound CONNECT to complete.
    /// </summary>
    public int ConnectTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum time a session (TCP relay or UDP association) may remain idle
    /// (no bytes transferred in either direction) before being torn down.
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Maximum idle time for a UDP association before its socket is released.
    /// </summary>
    public int UdpIdleTimeoutSeconds { get; set; } = 60;

    public bool EnableUdpAssociate { get; set; } = true;

    public Socks5TlsOptions Tls { get; set; } = new();

    public TimeSpan HandshakeTimeout => TimeSpan.FromSeconds(Math.Max(1, HandshakeTimeoutSeconds));

    public TimeSpan ConnectTimeout => TimeSpan.FromSeconds(Math.Max(1, ConnectTimeoutSeconds));

    public TimeSpan IdleTimeout => TimeSpan.FromSeconds(Math.Max(1, IdleTimeoutSeconds));

    public TimeSpan UdpIdleTimeout => TimeSpan.FromSeconds(Math.Max(1, UdpIdleTimeoutSeconds));
}
