# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Tether contributors.

<#
.SYNOPSIS
    Installs Tether for Space Engineers 2 on Windows.

.DESCRIPTION
    Copies Tether's DLLs to a folder (default: %LOCALAPPDATA%\Tether) and prints the Steam launch option to
    paste. It does not modify Steam, the game files, or the registry.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\install.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    # Folder containing Tether.dll. Default: the repo build output, else this script's folder.
    [string]$From,

    # Where to install. Avoid folders that need administrator rights.
    [string]$Destination = [IO.Path]::Combine($env:LOCALAPPDATA, 'Tether'),

    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$launchOption = "-plugins:`"$([IO.Path]::Combine($Destination, 'Tether.dll'))`""
$logs = [IO.Path]::Combine($env:APPDATA, 'SpaceEngineers2', 'Tether')

if ($Uninstall) {
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
        Write-Host "Removed $Destination"
    }
    Write-Host "Also remove $launchOption from the game's launch options in Steam."
    Write-Host "Tether's logs and settings are left in $logs (delete them if you like)."
    return
}

if (-not $From) {
    $repoOutput = [IO.Path]::Combine($PSScriptRoot, '..', 'src', 'Tether.Plugin', 'bin', 'Release', 'net9.0')
    if (Test-Path -LiteralPath ([IO.Path]::Combine($repoOutput, 'Tether.dll'))) {
        $From = $repoOutput
    }
    else {
        $From = $PSScriptRoot
    }
}

if (-not (Test-Path -LiteralPath ([IO.Path]::Combine($From, 'Tether.dll')))) {
    throw "Tether.dll not found in '$From'. Build it first: dotnet build src\Tether.Plugin -c Release"
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Get-ChildItem -LiteralPath $From -Filter '*.dll' | Copy-Item -Destination $Destination -Force
foreach ($notice in 'LICENSE', 'LICENSE-EXCEPTION.md') {
    foreach ($dir in $From, ([IO.Path]::Combine($PSScriptRoot, '..'))) {
        $candidate = [IO.Path]::Combine($dir, $notice)
        if (Test-Path -LiteralPath $candidate) {
            Copy-Item -LiteralPath $candidate -Destination $Destination -Force
            break
        }
    }
}

Write-Host "Installed Tether to ${Destination}:"
Get-ChildItem -LiteralPath $Destination | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host ''
Write-Host 'Now set the launch option in Steam: Space Engineers 2 > Properties > General > Launch Options:'
Write-Host ''
Write-Host "    $launchOption"
Write-Host ''
Write-Host "Tether's log will be at: $([IO.Path]::Combine($logs, 'tether.log'))"
