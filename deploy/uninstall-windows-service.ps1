<#
.SYNOPSIS
    Stops and removes the Socks5Proxy Windows Service.
#>
param(
    [string]$ServiceName = "Socks5Proxy"
)

$ErrorActionPreference = "Stop"

sc.exe stop $ServiceName
sc.exe delete $ServiceName

Write-Host "Service '$ServiceName' removed."
