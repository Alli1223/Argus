<#
.SYNOPSIS
    Installs the Argus agent as a Windows service.

.DESCRIPTION
    Run in an elevated PowerShell (Run as administrator):

    & ([scriptblock]::Create((Invoke-RestMethod https://argus.example.com/downloads/install.ps1))) `
        -Server https://argus.example.com -Token argus_et_...

.PARAMETER Server
    The Argus server the agent reports to.

.PARAMETER Token
    Enrollment token from the web UI. Required for a first install; reinstalling an
    already registered agent keeps its identity.

.PARAMETER Binary
    Install this argus-agent.exe instead of downloading it from the server.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $Server,

    [string] $Token,

    [string] $Binary
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 may still default to TLS 1.0.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$identity = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this in an elevated PowerShell (Run as administrator).'
}

$Server = $Server.TrimEnd('/')
if ($Server -notmatch '^https?://') {
    throw "-Server must be the server's http(s) address, e.g. https://argus.example.com"
}
if ($Token -and $Token -notmatch '^argus_et_[A-Za-z0-9_-]+$') {
    throw '-Token does not look like an enrollment token (they start with argus_et_).'
}

$serviceName = 'ArgusAgent'
$installDir = Join-Path $env:ProgramFiles 'Argus Agent'
$dataDir = Join-Path $env:ProgramData 'Argus\Agent'
$configPath = Join-Path $dataDir 'agent.json'
$exePath = Join-Path $installDir 'argus-agent.exe'

if (-not $Binary) {
    $Binary = Join-Path $env:TEMP 'argus-agent.exe'
    Write-Host '==> Downloading the agent'
    Invoke-WebRequest -Uri "$Server/downloads/agent/win-x64/argus-agent.exe" -OutFile $Binary -UseBasicParsing
}
if (-not (Test-Path $Binary)) {
    throw "Agent binary not found: $Binary"
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
}

Write-Host "==> Installing into $installDir"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
Copy-Item -Path $Binary -Destination $exePath -Force

# The data folder will hold the agent's key, so only SYSTEM and Administrators may open it.
# (Well-known SIDs, so this works whatever language Windows is installed in.)
& icacls.exe $dataDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null

if ($Token -or -not (Test-Path $configPath)) {
    if (-not $Token) {
        throw "-Token is required for a first install (create one under 'Add a system' in the web UI)."
    }
    Write-Host "==> Writing $configPath"
    $config = [ordered]@{ ServerUrl = $Server; EnrollmentToken = $Token } | ConvertTo-Json
    [IO.File]::WriteAllText($configPath, $config)
}

if (-not $existing) {
    New-Service -Name $serviceName `
        -DisplayName 'Argus Agent' `
        -Description 'Reports this computer''s health to the Argus server.' `
        -BinaryPathName "`"$exePath`" run" `
        -StartupType Automatic | Out-Null
}

# Restart the agent if it ever stops unexpectedly.
& sc.exe failure $serviceName reset= 86400 actions= restart/10000/restart/10000/restart/60000 | Out-Null

Start-Service -Name $serviceName
Write-Host "==> Done. The agent reports to $Server and shows up in the web UI within a minute."
Write-Host "    Status: Get-Service $serviceName"
