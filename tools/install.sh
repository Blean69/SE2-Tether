#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Tether contributors.
#
# Tether installer for Linux (Steam + Proton).
#
# What it does, and nothing else:
#   1. finds your Space Engineers 2 Steam library and the game's Proton prefix
#   2. copies Tether's DLLs into the prefix as C:\Tether
#   3. prints the launch option for you to paste into Steam
#
# It never edits Steam's configuration, the game files, or anything outside the prefix's C:\Tether folder.
# Native Linux (Pulsar + LinuxCompat) users install Tether through Pulsar instead.

set -euo pipefail

APP_ID=1133870
FROM=""
STEAM_ROOT=""
UNINSTALL=0

usage() {
    cat <<'EOF'
Usage: tools/install.sh [--from DIR] [--steam DIR] [--uninstall]

  --from DIR    Folder containing Tether.dll. Default: the plugin/ folder next to this script (test kit),
                else the repo build output (src/Tether.Plugin/bin/Release/net9.0).
  --steam DIR   Steam root folder. Default: auto-detect (~/.steam/steam, ~/.local/share/Steam, Flatpak).
  --uninstall   Remove Tether from the Proton prefix.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --from) FROM="${2:?--from needs a folder}"; shift 2 ;;
        --steam) STEAM_ROOT="${2:?--steam needs a folder}"; shift 2 ;;
        --uninstall) UNINSTALL=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

die() { echo "error: $*" >&2; exit 1; }

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

steam_roots() {
    if [[ -n "$STEAM_ROOT" ]]; then
        echo "$STEAM_ROOT"
        return
    fi
    local candidate
    for candidate in "$HOME/.steam/steam" "$HOME/.local/share/Steam" "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"; do
        [[ -d "$candidate/steamapps" ]] && echo "$candidate"
    done
}

# Prints every library folder Steam knows about: the root itself plus "path" entries in libraryfolders.vdf.
library_folders() {
    local root="$1"
    echo "$root"
    local vdf="$root/steamapps/libraryfolders.vdf"
    if [[ -f "$vdf" ]]; then
        sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$vdf"
    fi
}

find_game_library() {
    local root lib
    while IFS= read -r root; do
        while IFS= read -r lib; do
            if [[ -f "$lib/steamapps/appmanifest_${APP_ID}.acf" ]]; then
                echo "$lib"
                return 0
            fi
        done < <(library_folders "$root")
    done < <(steam_roots)
    return 1
}

library="$(find_game_library)" || die "Space Engineers 2 (app $APP_ID) not found in any Steam library. Use --steam to point at your Steam folder."
drive_c="$library/steamapps/compatdata/$APP_ID/pfx/drive_c"
[[ -d "$drive_c" ]] || die "No Proton prefix at $drive_c. Start Space Engineers 2 once with Proton, quit, then run this again."
dest="$drive_c/Tether"
logs="$drive_c/users/steamuser/AppData/Roaming/SpaceEngineers2/Tether"

if [[ "$UNINSTALL" -eq 1 ]]; then
    rm -rf -- "$dest"
    echo "Removed $dest"
    echo "Also remove '-plugins:C:\\Tether\\Tether.dll' from the game's launch options in Steam."
    echo "Tether's logs and settings are left in $logs (delete them if you like)."
    exit 0
fi

if [[ -z "$FROM" ]]; then
    for candidate in "$script_dir/plugin" "$script_dir/../src/Tether.Plugin/bin/Release/net9.0" "$script_dir"; do
        if [[ -f "$candidate/Tether.dll" ]]; then
            FROM="$candidate"
            break
        fi
    done
fi
FROM="${FROM:-$script_dir}"
[[ -f "$FROM/Tether.dll" ]] || die "Tether.dll not found in $FROM. Build it first: dotnet build src/Tether.Plugin -c Release"

mkdir -p -- "$dest"
cp -f -- "$FROM"/*.dll "$dest/"
for notice in LICENSE LICENSE-EXCEPTION.md; do
    for dir in "$FROM" "$script_dir" "$script_dir/.."; do
        if [[ -f "$dir/$notice" ]]; then
            cp -f -- "$dir/$notice" "$dest/"
            break
        fi
    done
done

echo "Installed Tether to $dest:"
(cd "$dest" && ls -1)
echo
echo "Now set the launch option in Steam: Space Engineers 2 > Properties > General > Launch Options:"
echo
echo "    -plugins:C:\\Tether\\Tether.dll"
echo
echo "Tether's log will be at: $logs/tether.log"
