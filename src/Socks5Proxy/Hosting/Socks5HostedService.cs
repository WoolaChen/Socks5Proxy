using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Socks5Proxy.Configuration;
using Socks5Proxy.Server;

namespace Socks5Proxy.Hosting;

/// <summary>
/// Hosts the <see cref="Socks5Server"/> as a <see cref="BackgroundService"/> so it
/// can run under the generic host, a Windows Service, or a systemd unit.
/// </summary>
public sealed class Socks5HostedService : BackgroundService
{
    private readonly Socks5ServerOptions _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<Socks5HostedService> _logger;

    public Socks5HostedService(IOptions<Socks5ServerOptions> options, ILoggerFactory loggerFactory, ILogger<Socks5HostedService> logger)
    {
        _options = options.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var server = new Socks5Server(_options, _loggerFactory);

        try
        {
            await server.RunAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "SOCKS5 server terminated unexpectedly.");
            throw;
        }
    }
}
