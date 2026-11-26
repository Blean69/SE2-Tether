#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Tether contributors.
#
# Checks a Tether release kit against this checkout. Check out the release's tag first.
#
#   1. SHA256SUMS lists exactly the files in the zip, with the right checksums.
#   2. The kit is rebuilt with tools/package.sh, and every rebuilt file must match byte for byte.
#   3. 0Harmony.dll must be identical to the one in the official Lib.Harmony package on nuget.org.
#
# Tether.Game.dll compiles against Space Engineers 2's own DLLs. Where the game is installed, it is rebuilt
# and compared too (use the same game version as the release). Elsewhere, as in CI, it is reported as not
# checked.
#
# Usage: tools/verify-release.sh path/to/tether-<version>.zip

set -euo pipefail

if [[ $# -ne 1 || ! -f "$1" ]]; then
    echo "usage: $0 path/to/tether-<version>.zip" >&2
    exit 2
fi

zip="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
base="$(basename "$zip" .zip)"
version="${base#tether-}"
if [[ "$base" == "$version" ]]; then
    echo "error: expected a file named tether-<version>.zip" >&2
    exit 2
fi

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
if [[ -n "$(git status --porcelain --untracked-files=no)" ]]; then
    echo "error: this checkout has uncommitted changes; check out the release's tag cleanly first" >&2
    exit 2
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "Verifying $base.zip against commit $(git rev-parse HEAD) ($(git describe --tags --always))"

if [[ -f "$zip.sha256" ]]; then
    (cd "$(dirname "$zip")" && sha256sum --quiet -c "$base.zip.sha256")
    echo "OK  the zip matches $base.zip.sha256."
fi

# 1. The published kit's own checksums are complete and correct.
unzip -q "$zip" -d "$work/published"
pub="$work/published/$base"
[[ -f "$pub/SHA256SUMS" ]] || { echo "error: $base/SHA256SUMS is missing from the zip" >&2; exit 1; }
(cd "$pub" && sha256sum --quiet -c SHA256SUMS)
if ! diff <(cd "$pub" && find . -type f ! -name SHA256SUMS | sed 's|^\./||' | LC_ALL=C sort) \
          <(cut -c67- "$pub/SHA256SUMS" | LC_ALL=C sort); then
    echo "error: SHA256SUMS doesn't list exactly the files in the zip (diff above)" >&2
    exit 1
fi
echo "OK  SHA256SUMS lists exactly the $(wc -l < "$pub/SHA256SUMS") files in the zip, and they match."

# 2. Rebuild from source and compare.
if ! TETHER_VERSION="$version" tools/package.sh "$work/rebuilt" > "$work/package.log" 2>&1; then
    cat "$work/package.log" >&2
    echo "error: rebuilding the kit failed" >&2
    exit 1
fi
unzip -q "$work/rebuilt/$base.zip" -d "$work/rebuilt"
reb="$work/rebuilt/$base"

game_only='^[0-9a-f]{64}  plugin/(Tether\.Game|0Harmony)\.dll$'
if [[ -f "$reb/plugin/Tether.Game.dll" ]]; then
    expected="$pub/SHA256SUMS"
else
    grep -v -E "$game_only" "$pub/SHA256SUMS" > "$work/expected" || true
    expected="$work/expected"
fi
if ! diff "$expected" "$reb/SHA256SUMS"; then
    echo "error: the rebuilt files differ from the published ones (diff above: < published, > rebuilt)" >&2
    exit 1
fi
echo "OK  $(wc -l < "$reb/SHA256SUMS") files rebuilt from source match byte for byte."

# 3. Harmony is the official package's DLL, unmodified.
if [[ -f "$pub/plugin/0Harmony.dll" ]]; then
    hv="$(sed -n 's:.*Include="Lib.Harmony" Version="\([^"]*\)".*:\1:p' src/Tether.Game/Tether.Game.csproj)"
    curl -fsSL "https://api.nuget.org/v3-flatcontainer/lib.harmony/$hv/lib.harmony.$hv.nupkg" -o "$work/harmony.nupkg"
    official="$(unzip -p "$work/harmony.nupkg" lib/net9.0/0Harmony.dll | sha256sum | cut -d' ' -f1)"
    shipped="$(sha256sum "$pub/plugin/0Harmony.dll" | cut -d' ' -f1)"
    if [[ "$official" != "$shipped" ]]; then
        echo "error: plugin/0Harmony.dll differs from Lib.Harmony $hv on nuget.org" >&2
        exit 1
    fi
    echo "OK  plugin/0Harmony.dll is identical to Lib.Harmony $hv from nuget.org."
fi

if [[ -f "$pub/plugin/Tether.Game.dll" && ! -f "$reb/plugin/Tether.Game.dll" ]]; then
    echo "--  plugin/Tether.Game.dll was not rebuilt: it compiles against Space Engineers 2, which isn't installed here."
    echo "    To check it, run this script on a machine with the game, on the release's game version."
fi
echo "Verified."
