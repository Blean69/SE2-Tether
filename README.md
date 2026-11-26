# Tether

**Tether** is an unofficial, fully open-source co-op multiplayer plugin for **Space Engineers 2**. It
runs on Windows and Linux.

**Status: first test kit (Phase 0).** You can't play multiplayer with it yet. The protocol, transport
and session layers are written and tested. The plugin loads into the game, reports what we need to
know, and can host or join a network test session from inside the game. Each CI run produces a
`tether-test-kit` zip; see [`docs/TESTING.md`](docs/TESTING.md).

- Design plan: [`docs/PLAN.md`](docs/PLAN.md)
- Research notes: [`docs/research/2026-11-landscape.md`](docs/research/2026-11-landscape.md)
- First run with the game (Phase 0): [`docs/PHASE0.md`](docs/PHASE0.md)

## What v1 aims at

Small co-op sessions of up to 4 players:

- One player hosts from inside the game. Their game is the authority.
- Friends join over Steam peer-to-peer, using Valve's relay network. That means no port forwarding and
  no dedicated server.
- Direct IP is also supported, for LAN play and for testing.

## Principles

- **All source is public**, under GPL-3.0-or-later with a narrow game-linking exception (see
  [`LICENSE-EXCEPTION.md`](LICENSE-EXCEPTION.md)).
- Builds are reproducible, so anyone can check that a download matches the code.
- No account gates and no telemetry.
- No closed executables. Install scripts are short and readable.
- Windows and Linux are equals:
  - Windows;
  - Linux via Proton;
  - native Linux via Pulsar and LinuxCompat.
- We never redistribute Keen Software House files.

## Building

You need the .NET 10 SDK. The plugin targets `net9.0`, the runtime the game uses, and the tests also
need the .NET 9 runtime.

```sh
dotnet test tests/Tether.Core.Tests     # core tests; no game needed
tools/package.sh                        # the test kit zip; no game needed, byte-identical to CI's
```

The plugin builds without the game, using Tether's interop declarations. If a game install is found,
the plugin compiles against it instead, which gives stronger compile-time checks:

- **Windows:** found through Steam's registry entry.
- **Linux:** found in `~/.steam/steam/steamapps/common/SpaceEngineers2/Game2`.

To point the build somewhere else, copy `Directory.Build.props.user.example` to
`Directory.Build.props.user` and set `Game2Dir`.

## Repository layout

| Path | What it is |
|---|---|
| `src/Tether.Core` | Game-independent code: protocol, transports (loopback and LiteNetLib), host/client sessions, network-test node |
| `src/Tether.Plugin` | `Tether.dll`, the tiny entry point the game loads (no dependencies besides the game's `IPlugin`) |
| `src/Tether.Runtime` | Everything Tether does in the game process: diagnostics, network self-test, and later the game adapter |
| `src/Tether.NetTest` | `tether-nettest`, a command-line network tester (no game needed) |
| `ref/VRage.Core.Interop` | Hand-written declarations of the two game types Tether compiles against. Not Keen code; never shipped. |
| `tests/` | Unit, fuzz and integration tests; a stand-in game process for end-to-end tests |
| `tools/` | Install, package (`package.sh`) and end-to-end test scripts |
| `docs/` | Plan, research notes, Phase 0 runbook, testing guide |

## Legal

This is an unofficial community project, not affiliated with or endorsed by Keen Software House.
Space Engineers is a trademark of Keen Software House s.r.o. You need a legal copy of Space Engineers 2
to use this plugin.
