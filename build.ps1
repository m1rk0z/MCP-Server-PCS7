# Builds everything:
#   release\                  Pcs7McpServer.exe for the Claude Code PC (self-contained .NET 8 x86) + cfcreader\ for local mode
#   release\agent\Pcs7Agent-<version>.zip   package to copy to the PCS 7 machine / VM (.NET Framework 4.8 x86)
#
# Usage:  .\build.ps1 [-Release <output folder>]   (needs the .NET 8 SDK: winget install Microsoft.DotNet.SDK.8)
param([string]$Release = (Join-Path $PSScriptRoot 'release'))
$ErrorActionPreference = 'Stop'
$src = Join-Path $PSScriptRoot 'src'
$release = $Release

# First dotnet that actually has an SDK (a machine-wide runtime-only install may come first in PATH).
$dotnet = @((Get-Command dotnet -All -ErrorAction SilentlyContinue).Source) + (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe') |
    Where-Object { $_ -and (Test-Path $_) -and (& $_ --list-sdks 2>$null) } | Select-Object -First 1
if (-not $dotnet) { throw '.NET 8 SDK not found (winget install Microsoft.DotNet.SDK.8)' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'

function Publish($project, $out) {
    & $dotnet publish (Join-Path $src $project) -c Release -o $out --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "publish $project failed" }
}

# Stop before deleting anything if a Claude Code session is still running the server from the output folder.
$running = Get-CimInstance Win32_Process -Filter "Name='Pcs7McpServer.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith([IO.Path]::GetFullPath($release), 'OrdinalIgnoreCase') }
if ($running) { throw "Pcs7McpServer.exe is running from $release (PID $($running.ProcessId -join ', ')): close the Claude Code sessions using it first." }

Write-Host '== MCP server (PC)'
Get-ChildItem $release -Exclude 'agent' -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
Publish 'Pcs7Mcp' $release
Publish 'Pcs7CfcReader' (Join-Path $release 'cfcreader')

Write-Host '== Agent (PCS 7 machine)'
$version = ([xml](Get-Content (Join-Path $src 'Pcs7Agent\Pcs7Agent.csproj'))).Project.PropertyGroup.Version | Select-Object -First 1
$stage = Join-Path ([IO.Path]::GetTempPath()) "Pcs7Agent-$version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
Publish 'Pcs7Agent' $stage
Publish 'Pcs7CfcReader' (Join-Path $stage 'cfcreader')
Get-ChildItem $stage -Recurse -Include *.pdb, *.xml | Remove-Item -Force

$agentDir = Join-Path $release 'agent'
New-Item -ItemType Directory -Force $agentDir | Out-Null
Get-ChildItem $agentDir -Filter 'Pcs7Agent-*.zip' | Remove-Item -Force
$zip = Join-Path $agentDir "Pcs7Agent-$version.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Get-ChildItem $release -Filter *.pdb -Recurse | Remove-Item -Force

Write-Host ''
Write-Host "PC server : $release\Pcs7McpServer.exe"
Write-Host "VM agent  : $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
