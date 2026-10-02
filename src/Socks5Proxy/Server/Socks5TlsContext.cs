using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Socks5Proxy.Configuration;

namespace Socks5Proxy.Server;

/// <summary>
/// Creates the optional TLS control-channel wrapper used for SOCKS5-over-TLS.
/// </summary>
public sealed class Socks5TlsContext
{
    private readonly X509Certificate2? _certificate;
    private readonly bool _requireClientCertificate;
    private readonly ILogger _logger;

    private Socks5TlsContext(X509Certificate2? certificate, bool requireClientCertificate, ILogger logger)
    {
        _certificate = certificate;
        _requireClientCertificate = requireClientCertificate;
        _logger = logger;
    }

    public bool Enabled => _certificate is not null;

    public static Socks5TlsContext Create(Socks5TlsOptions options, ILogger logger)
    {
        if (!options.Enabled)
        {
            return new Socks5TlsContext(null, false, logger);
        }

        if (string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            throw new InvalidOperationException("Socks5:Tls:Enabled is true but no CertificatePath was configured.");
        }

#pragma warning disable SYSLIB0057 // X509Certificate2 constructor is obsolete starting in .NET 9; net8.0 target still requires it.
        var certificate = new X509Certificate2(options.CertificatePath, options.CertificatePassword);
#pragma warning restore SYSLIB0057

        return new Socks5TlsContext(certificate, options.RequireClientCertificate, logger);
    }

    public async Task<Stream> WrapAsync(Stream innerStream, CancellationToken cancellationToken)
    {
        if (_certificate is null)
        {
            return innerStream;
        }

        var sslStream = new SslStream(innerStream, leaveInnerStreamOpen: false, UserCertificateValidationCallback);
        var sslOptions = new SslServerAuthenticationOptions
        {
            ServerCertificate = _certificate,
            ClientCertificateRequired = _requireClientCertificate,
            EnabledSslProtocols = SslProtocols.None, // let the OS negotiate the best protocol
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };

        await sslStream.AuthenticateAsServerAsync(sslOptions, cancellationToken).ConfigureAwait(false);
        return sslStream;
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (!_requireClientCertificate)
        {
            return true;
        }

        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            _logger.LogWarning("Rejecting client certificate due to policy errors: {Errors}", sslPolicyErrors);
            return false;
        }

        return certificate is not null;
    }
}
