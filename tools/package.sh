#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Tether contributors.
#
# Builds the Tether test kit zip:
#   plugin/    Tether.dll + Tether.Runtime.dll + Tether.Core.dll + LiteNetLib.dll, built from this repository alone;
#              plus Tether.Game.dll + 0Harmony.dll (the in-game multiplayer layer) when the game is installed here
#   nettest/   tether-nettest (framework-dependent; runs on the .NET 9+ runtime)
#   install scripts, nettest launchers, licenses, README-TESTING.md, SHA256SUMS
#
# The build is reproducible: the same commit gives byte-identical DLLs and an identical zip on any machine
# (file times are pinned to the commit time, order and permissions are normalised). CI runs exactly this.
#
# Usage: tools/package.sh [OUTPUT_DIR]      (default: artifacts/)
#        TETHER_VERSION=0.1.0-test1 tools/package.sh   names the zip after a release instead of the commit
#        TETHER_REQUIRE_GAME=1 tools/package.sh        fails instead of leaving out the multiplayer layer

set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="${1:-$repo/artifacts}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"

cd "$repo"
prefix="$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' Directory.Build.props)"
commit="$(git rev-parse --short=10 HEAD)"
version="${TETHER_VERSION:-${prefix}-dev.${commit}}"
if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
    version="${version}.dirty"
    echo "warning: uncommitted changes; this package won't match a CI build of $commit" >&2
fi

name="tether-${version}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
stage="$work/$name"
mkdir -p "$stage"

common=(-c Release -p:ContinuousIntegrationBuild=true -p:Deterministic=true)

echo "Building plugin (against Tether's interop declarations, not game files)..."
dotnet publish src/Tether.Plugin "${common[@]}" -p:TetherUseGameReferences=false -o "$stage/plugin" >/dev/null
echo "Building tether-nettest..."
dotnet publish src/Tether.NetTest "${common[@]}" -p:UseAppHost=false -o "$stage/nettest" >/dev/null

# The in-game multiplayer layer compiles against the real game, so it can only be built where the game is
# installed. Without it the kit still works, but only with the diagnostics and network tests.
game2="$(dotnet msbuild src/Tether.Game/Tether.Game.csproj -getProperty:Game2Dir 2>/dev/null || true)"
if [[ -n "$game2" && -f "$game2/SpaceEngineers2.dll" ]]; then
    echo "Building Tether.Game (against the game in $game2)..."
    dotnet publish src/Tether.Game "${common[@]}" -o "$work/game" >/dev/null
    cp "$work/game/Tether.Game.dll" "$work/game/0Harmony.dll" "$stage/plugin/"
elif [[ "${TETHER_REQUIRE_GAME:-0}" == "1" ]]; then
    echo "error: Space Engineers 2 not found, and TETHER_REQUIRE_GAME=1 needs the in-game multiplayer layer (Tether.Game.dll)." >&2
    exit 1
else
    echo "warning: Space Engineers 2 not found; the kit will NOT include the in-game multiplayer layer (Tether.Game.dll)." >&2
fi

# Keep only what runs: no XML docs; the plugin's deps/runtimeconfig files are unused by the game.
find "$stage" -name '*.xml' -delete
rm -f "$stage"/plugin/*.deps.json "$stage"/plugin/*.runtimeconfig.json "$stage"/plugin/*.pdb "$stage"/nettest/*.pdb

# Never ship game assemblies, real or declared.
if find "$stage" \( -name 'VRage*.dll' -o -name 'Game2*.dll' -o -name 'SpaceEngineers2*.dll' -o -name 'Steamworks*.dll' \) | grep -q .; then
    echo "error: a game assembly ended up in the package" >&2
    exit 1
fi

cp tools/install.sh tools/install.ps1 LICENSE LICENSE-EXCEPTION.md THIRD-PARTY-NOTICES.md "$stage/"
cp docs/TESTING.md "$stage/README-TESTING.md"

cat > "$stage/nettest.sh" <<'EOF'
#!/bin/sh
# Runs tether-nettest. Needs the .NET runtime 9 or newer (https://dotnet.microsoft.com/download/dotnet).
command -v dotnet >/dev/null 2>&1 || { echo "tether-nettest needs the .NET runtime 9 or newer: https://dotnet.microsoft.com/download/dotnet" >&2; exit 1; }
exec dotnet "$(dirname "$0")/nettest/tether-nettest.dll" "$@"
EOF
printf '@echo off\r\nrem Installs Tether for Space Engineers 2. Double-click to run; it only runs install.ps1 next to this file.\r\npowershell -NoProfile -ExecutionPolicy Bypass -File "%%~dp0install.ps1"\r\necho.\r\npause\r\n' > "$stage/Install Tether.cmd"
printf '@echo off\r\nrem Removes Tether. Double-click to run; it only runs install.ps1 -Uninstall next to this file.\r\npowershell -NoProfile -ExecutionPolicy Bypass -File "%%~dp0install.ps1" -Uninstall\r\necho.\r\npause\r\n' > "$stage/Uninstall Tether.cmd"
printf '@echo off\r\nrem Runs tether-nettest. Needs the .NET runtime 9 or newer, which Space Engineers 2 already installs.\r\nwhere dotnet >nul 2>nul || (echo tether-nettest needs the .NET runtime 9 or newer: https://dotnet.microsoft.com/download/dotnet & exit /b 1)\r\ndotnet "%%~dp0nettest\\tether-nettest.dll" %%*\r\n' > "$stage/nettest.cmd"

# Normalise permissions, then checksum everything.
find "$stage" -type d -exec chmod 755 {} +
find "$stage" -type f -exec chmod 644 {} +
chmod 755 "$stage/install.sh" "$stage/nettest.sh"
(cd "$stage" && find . -type f -print0 | LC_ALL=C sort -z | xargs -0 sha256sum | sed 's|  \./|  |' > "$work/SHA256SUMS")
mv "$work/SHA256SUMS" "$stage/SHA256SUMS"
chmod 644 "$stage/SHA256SUMS"

# Reproducible zip: fixed timestamps (commit time, UTC), sorted entries, no extra attributes.
epoch="$(git log -1 --format=%ct)"
find "$stage" -exec touch -h -d "@$epoch" {} +
rm -f "$out/$name.zip"
(cd "$work" && find "$name" -type f | LC_ALL=C sort | TZ=UTC zip -X -D -q "$out/$name.zip" -@)
(cd "$out" && sha256sum "$name.zip" > "$name.zip.sha256")

echo "Package: $out/$name.zip"
cat "$out/$name.zip.sha256"
echo "Contents:"
sed 's/^/  /' "$stage/SHA256SUMS"
