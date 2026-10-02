namespace Socks5Proxy.Configuration;

/// <summary>
/// Options controlling an optional TLS control channel, allowing the SOCKS5
/// handshake and relayed traffic to be wrapped in TLS (SOCKS5-over-TLS).
/// </summary>
public sealed class Socks5TlsOptions
{
    /// <summary>
    /// When true, incoming TCP connections are expected to perform a TLS
    /// handshake before any SOCKS5 negotiation takes place.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Path to a PFX/PKCS12 certificate file used as the server certificate.
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// Password protecting the certificate file, if any.
    /// </summary>
    public string CertificatePassword { get; set; } = string.Empty;

    /// <summary>
    /// When true, clients must present a valid client certificate.
    /// </summary>
    public bool RequireClientCertificate { get; set; }
}
