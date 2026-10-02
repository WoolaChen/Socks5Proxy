using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;
using Socks5Proxy.Configuration;
using Socks5Proxy.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables(prefix: "SOCKS5PROXY_");
builder.Configuration.AddCommandLine(args);

builder.Services.Configure<Socks5ServerOptions>(builder.Configuration.GetSection(Socks5ServerOptions.SectionName));
builder.Services.AddHostedService<Socks5HostedService>();

if (WindowsServiceHelpers.IsWindowsService())
{
    builder.Services.AddWindowsService(o => o.ServiceName = "Socks5Proxy");
}

if (SystemdHelpers.IsSystemdService())
{
    builder.Services.AddSystemd();
}

var host = builder.Build();
await host.RunAsync();
