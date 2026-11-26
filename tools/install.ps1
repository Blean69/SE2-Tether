# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Tether contributors.

<#
.SYNOPSIS
    Installs Tether for Space Engineers 2 on Windows.

.DESCRIPTION
    Copies Tether's DLLs to a folder (default: %LOCALAPPDATA%\Tether), then shows the Steam launch option
    and copies it to the clipboard. It does not modify Steam, the game files, or the registry.

    In the test kit, double-click "Install Tether.cmd", which runs this script.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    # Folder containing Tether.dll. Default: the plugin folder next to this script (test kit), else the repo build output.
    [string]$From,

    # Where to install. Avoid folders that need administrator rights.
    [string]$Destination = [IO.Path]::Combine($env:LOCALAPPDATA, 'Tether'),

    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$launchOption = "-plugins:`"$([IO.Path]::Combine($Destination, 'Tether.dll'))`""
$logs = [IO.Path]::Combine($env:APPDATA, 'SpaceEngineers2', 'Tether')

# Windows locks DLLs the running game has loaded, so installing or removing over them would fail half-way.
if (Get-Process -Name 'SpaceEngineers2' -ErrorAction SilentlyContinue) {
    throw 'Space Engineers 2 is running. Quit the game, then run this again.'
}

if ($Uninstall) {
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
        Write-Host "Removed $Destination"
    }
    Write-Host "Also clear the game's launch options in Steam ($launchOption and any -tether: options)."
    Write-Host "Tether's logs and settings are left in $logs (delete them if you like)."
    return
}

if (-not $From) {
    $candidates = @(
        [IO.Path]::Combine($PSScriptRoot, 'plugin'),
        [IO.Path]::Combine($PSScriptRoot, '..', 'src', 'Tether.Plugin', 'bin', 'Release', 'net9.0'),
        $PSScriptRoot)
    $From = $candidates | Where-Object { Test-Path -LiteralPath ([IO.Path]::Combine($_, 'Tether.dll')) } | Select-Object -First 1
    if (-not $From) { $From = $PSScriptRoot }
}

if (-not (Test-Path -LiteralPath ([IO.Path]::Combine($From, 'Tether.dll')))) {
    throw "Tether.dll not found in '$From'. Build it first: dotnet build src\Tether.Plugin -c Release"
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Get-ChildItem -LiteralPath $From -Filter '*.dll' | Copy-Item -Destination $Destination -Force
foreach ($notice in 'LICENSE', 'LICENSE-EXCEPTION.md') {
    foreach ($dir in $From, $PSScriptRoot, ([IO.Path]::Combine($PSScriptRoot, '..'))) {
        $candidate = [IO.Path]::Combine($dir, $notice)
        if (Test-Path -LiteralPath $candidate) {
            Copy-Item -LiteralPath $candidate -Destination $Destination -Force
            break
        }
    }
}

Write-Host "Installed Tether to ${Destination}:"
Get-ChildItem -LiteralPath $Destination | ForEach-Object { Write-Host "  $($_.Name)" }

# The options a first test needs: load the plugin and log in detail. README-TESTING.md lists the others.
$fullOptions = "$launchOption -tether:debug"
$copied = $false
try {
    Set-Clipboard -Value $fullOptions
    $copied = $true
}
catch {
    # No clipboard available (e.g. a remote shell); the text is printed below anyway.
}

Write-Host ''
Write-Host 'Last step: in Steam, right-click Space Engineers 2 > Properties > General > Launch Options, and paste:'
Write-Host ''
Write-Host "    $fullOptions" -ForegroundColor Cyan
Write-Host ''
if ($copied) {
    Write-Host '(Already copied to your clipboard: just press Ctrl+V in that box.)'
    Write-Host ''
}
Write-Host "Then start the game. Tether's log will be at: $([IO.Path]::Combine($logs, 'tether.log'))"
