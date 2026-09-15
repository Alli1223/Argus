<#
.SYNOPSIS
    Removes the Argus agent. With -Purge its configuration and identity are deleted too.

.DESCRIPTION
    The host stays listed in the web UI (with its history) until you delete it there.
#>
param(
    [switch] $Purge
)

$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this in an elevated PowerShell (Run as administrator).'
}

$serviceName = 'ArgusAgent'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
    }
    & sc.exe delete $serviceName | Out-Null
}

Remove-Item -Recurse -Force (Join-Path $env:ProgramFiles 'Argus Agent') -ErrorAction SilentlyContinue

if ($Purge) {
    Remove-Item -Recurse -Force (Join-Path $env:ProgramData 'Argus\Agent') -ErrorAction SilentlyContinue
}

Write-Host 'Argus agent removed.'
