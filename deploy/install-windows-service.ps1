<#
.SYNOPSIS
    Installs Socks5Proxy as a Windows Service.
.DESCRIPTION
    Publishes (if needed) and registers the SOCKS5 proxy as a Windows Service
    using sc.exe. Must be run from an elevated PowerShell prompt.
.PARAMETER InstallPath
    Directory containing the published Socks5Proxy.exe. Defaults to
    "C:\Services\Socks5Proxy".
#>
param(
    [string]$InstallPath = "C:\Services\Socks5Proxy",
    [string]$ServiceName = "Socks5Proxy"
)

$ErrorActionPreference = "Stop"

$exePath = Join-Path $InstallPath "Socks5Proxy.exe"
if (-not (Test-Path $exePath)) {
    throw "Could not find $exePath. Publish the project first, e.g.:`n  dotnet publish src/Socks5Proxy -c Release -o `"$InstallPath`""
}

sc.exe create $ServiceName binPath= "`"$exePath`"" start= auto
sc.exe description $ServiceName "Production SOCKS5 proxy server"
sc.exe start $ServiceName

Write-Host "Service '$ServiceName' installed and started."
