# Socks5Proxy

A production-oriented SOCKS5 proxy server written in C# for .NET 8.

## Features

- **SOCKS5 handshake** (RFC 1928) with pluggable authentication:
  - No-auth (`0x00`)
  - Username/password (RFC 1929, `0x02`) with constant-time credential comparison
- **CONNECT** command with:
  - Multi-address DNS resolution and automatic fail-over between resolved addresses
  - Configurable connect timeout
  - Bidirectional relay with an idle timeout independent of either peer
- **UDP ASSOCIATE** command with:
  - One dedicated UDP socket per association (dual-mode IPv4/IPv6 when supported)
  - Client-endpoint learning and source-address validation (basic anti-spoofing)
  - Target-endpoint tracking so replies are routed back correctly
  - Idle timeout independent from the TCP control connection, plus automatic
    teardown when the control connection closes
- **Production hardening**:
  - Global concurrent-connection limit (new connections are rejected once the
    limit is reached)
  - Handshake, connect, and idle timeouts (all configurable)
  - IPv6 support, including dual-mode listening on `::`
  - Strict, non-crashing error handling — malformed input from one client
    never takes down the server or other sessions
  - Structured logging via `Microsoft.Extensions.Logging`
  - Optional TLS control channel (SOCKS5-over-TLS), including optional mutual TLS
- **Hosting**:
  - Runs under the .NET Generic Host (`Microsoft.Extensions.Hosting`)
  - Installable as a **Windows Service** or a **systemd** unit
  - Docker image included

## Project layout

```text
Socks5Proxy.sln
src/Socks5Proxy/
  Program.cs                     Host bootstrap (console / Windows Service / systemd)
  appsettings.json                Default configuration
  Configuration/                  Strongly typed options (Socks5ServerOptions, Tls, Credentials)
  Protocol/                       Wire-format constants, enums, address codec, request model
  Networking/                     Stream read helpers, timeout helper
  Authentication/                 ISocks5Authenticator + no-auth/user-pass implementations
  Server/                         Socks5Server, Socks5ClientSession, Socks5Relay,
                                   Socks5UdpAssociation, Socks5TlsContext
  Hosting/                        Socks5HostedService (BackgroundService)
tests/Socks5Proxy.Tests/          xUnit unit + integration tests
deploy/                           systemd unit file, Windows service install scripts
Dockerfile
```

## Building and testing

```bash
dotnet build Socks5Proxy.sln
dotnet test Socks5Proxy.sln
```

## Running

```bash
dotnet run --project src/Socks5Proxy
```

By default the server listens on `[::]:1080` (dual-mode, so both IPv4 and
IPv6 clients can connect) with no-auth enabled.

### Configuration

Configuration is bound from the `Socks5` section of `appsettings.json`,
environment variables prefixed with `SOCKS5PROXY_` (e.g.
`SOCKS5PROXY_Socks5__ListenPort=1081`), and command-line arguments
(e.g. `--Socks5:ListenPort=1081`).

```json
{
  "Socks5": {
    "ListenAddress": "::",
    "ListenPort": 1080,
    "DualMode": true,
    "EnableNoAuth": true,
    "EnableUsernamePasswordAuth": false,
    "Credentials": [
      { "Username": "user", "Password": "pass" }
    ],
    "MaxConcurrentConnections": 512,
    "HandshakeTimeoutSeconds": 10,
    "ConnectTimeoutSeconds": 10,
    "IdleTimeoutSeconds": 120,
    "UdpIdleTimeoutSeconds": 60,
    "EnableUdpAssociate": true,
    "Tls": {
      "Enabled": false,
      "CertificatePath": "",
      "CertificatePassword": "",
      "RequireClientCertificate": false
    }
  }
}
```

| Setting | Description |
|---|---|
| `ListenAddress` / `ListenPort` | Bind address/port. Use `::` for dual-mode IPv4+IPv6. |
| `DualMode` | When `ListenAddress` is IPv6, accept IPv4 connections too. |
| `EnableNoAuth` / `EnableUsernamePasswordAuth` | Enable one or both auth methods. At least one must be enabled. |
| `Credentials` | List of accepted username/password pairs. |
| `MaxConcurrentConnections` | Hard cap on simultaneous sessions; excess connections are closed immediately. |
| `HandshakeTimeoutSeconds` | Max time to complete the greeting + auth sub-negotiation. |
| `ConnectTimeoutSeconds` | Max time for an outbound `CONNECT` to succeed. |
| `IdleTimeoutSeconds` | Max time a TCP relay may sit idle before being torn down. |
| `UdpIdleTimeoutSeconds` | Max idle time for a UDP association before its socket is released. |
| `EnableUdpAssociate` | Enable/disable the `UDP ASSOCIATE` command. |
| `Tls.*` | Optional TLS control channel (SOCKS5-over-TLS) — see below. |

### Usage examples

Plain CONNECT, no auth:

```bash
curl --proxy socks5://127.0.0.1:1080 https://example.com
```

With username/password auth:

```bash
curl --proxy socks5://127.0.0.1:1080 --proxy-user user:pass https://example.com
```

Resolve the hostname through the proxy (`socks5h`):

```bash
curl --proxy socks5h://127.0.0.1:1080 https://example.com
```

### TLS control channel (SOCKS5-over-TLS)

When `Tls.Enabled` is `true`, every accepted TCP connection must complete a
TLS handshake before any SOCKS5 negotiation takes place — this both encrypts
and authenticates the control channel and relayed traffic end-to-end between
client and proxy (classic "SOCKS5 over TLS"/stunnel-style use case).

```json
"Tls": {
  "Enabled": true,
  "CertificatePath": "/path/to/server.pfx",
  "CertificatePassword": "changeit",
  "RequireClientCertificate": false
}
```

Clients must perform a TLS handshake over the raw TCP connection before
speaking SOCKS5. Tools without native SOCKS5-over-TLS support typically need
a local TLS-terminating stub (e.g. `stunnel`/`socat`) in front of them.

## Deployment

### Docker

```bash
docker build -t socks5proxy .
docker run -d --name socks5proxy -p 1080:1080/tcp -p 1080:1080/udp socks5proxy
```

Override configuration with environment variables, e.g.:

```bash
docker run -d -p 1080:1080/tcp -p 1080:1080/udp \
  -e SOCKS5PROXY_Socks5__EnableUsernamePasswordAuth=true \
  -e SOCKS5PROXY_Socks5__EnableNoAuth=false \
  -e SOCKS5PROXY_Socks5__Credentials__0__Username=user \
  -e SOCKS5PROXY_Socks5__Credentials__0__Password=pass \
  socks5proxy
```

### Linux (systemd)

```bash
dotnet publish src/Socks5Proxy -c Release -o /opt/socks5proxy
sudo useradd --system --no-create-home --shell /usr/sbin/nologin socks5
sudo cp deploy/socks5proxy.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now socks5proxy
```

### Windows Service

```powershell
dotnet publish src/Socks5Proxy -c Release -o C:\Services\Socks5Proxy
.\deploy\install-windows-service.ps1
```

Uninstall with `.\deploy\uninstall-windows-service.ps1`.

## IPv6

The server binds `::` in dual-mode by default, so it accepts both IPv4 and
IPv6 control connections. `UDP ASSOCIATE` relay sockets likewise bind a
dual-mode IPv6 socket when the platform supports it (falling back to IPv4-only
otherwise), so UDP relaying works for both IPv4 and IPv6 targets regardless of
which family the client used to connect.
