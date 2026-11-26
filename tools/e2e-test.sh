#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Tether contributors.
#
# End-to-end test of an unpacked Tether test kit, on Linux or Windows (Git Bash). CI runs it on both.
#   1. tether-nettest host <-> tether-nettest join, over real UDP on localhost
#   2. the packaged plugin inside a stand-in game process (tests/Tether.Plugin.SmokeHost), hosting the
#      in-game network self-test, joined by tether-nettest
#
# Usage: tools/e2e-test.sh <unpacked kit folder> <path to Tether.Plugin.SmokeHost.dll>

set -euo pipefail
export MSYS_NO_PATHCONV=1 # Git Bash: don't rewrite arguments; we pass native paths ourselves.

kit="${1:?usage: e2e-test.sh <kit folder> <smoke host dll>}"
smoke="${2:?usage: e2e-test.sh <kit folder> <smoke host dll>}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Paths handed to dotnet must be native (C:\... on Windows).
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s\n' "$1"; fi; }
nettest() { dotnet "$(native "$kit/nettest/tether-nettest.dll")" "$@" </dev/null; }

fail() {
    echo "E2E FAILED: $*" >&2
    for f in "$work"/*.log "$work"/data/tether.log; do
        [[ -f "$f" ]] && { echo "----- $f" >&2; cat "$f" >&2; }
    done
    exit 1
}

echo "== 1. tether-nettest host <-> join"
nettest host --port 27161 --name E2EHost --password e2e --expect-chat "e2e hello" --exit-on-success --duration 40 --report 0 > "$work/host.log" 2>&1 &
host_pid=$!
sleep 3
nettest join 127.0.0.1:27161 --name E2EClient --password e2e --say "e2e hello" --duration 15 --report 0 > "$work/join.log" 2>&1 || fail "join exited with $?"
wait "$host_pid" || fail "host exited with $?"
grep -q "All expectations met" "$work/host.log" || fail "host did not receive the chat line"
grep -q "Welcome, E2EClient!" "$work/join.log" || fail "client did not get the host's welcome"
echo "   ok"

echo "== 2. packaged plugin in a stand-in game process, joined by tether-nettest"
mkdir -p "$work/data"
dotnet "$(native "$smoke")" "$(native "$kit/plugin/Tether.dll")" 20 \
    -tether:host-test=27162 -tether:name=E2EGame "-tether:data=$(native "$work/data")" > "$work/smoke.log" 2>&1 &
smoke_pid=$!
sleep 4
nettest join 127.0.0.1:27162 --name E2EJoiner --say "e2e ping" --expect-chat "inside Space Engineers 2" --exit-on-success --duration 20 --report 0 > "$work/join2.log" 2>&1 || fail "join to the in-game self-test exited with $?"
wait "$smoke_pid" || fail "smoke host exited with $?"
log="$work/data/tether.log"
[[ -f "$log" ]] || fail "plugin wrote no tether.log"
grep -q "Tether bootstrap complete" "$log" || fail "plugin did not finish bootstrapping"
assemblies="$(grep "Tether assemblies:" "$log" || true)"
for dll in Tether.Runtime Tether.Core; do
    [[ "$assemblies" == *"$dll "* ]] || fail "plugin dependency $dll was not loaded"
done
grep -q "<E2EJoiner> e2e ping" "$log" || fail "plugin's in-game host did not receive the chat line"
grep -q "Tether unloading" "$log" || fail "plugin did not unload cleanly"
if grep -q "\[Error" "$log"; then fail "plugin logged errors"; fi
echo "   ok"

echo "E2E PASSED"
