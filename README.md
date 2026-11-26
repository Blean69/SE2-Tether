# Tether

**Tether** is an unofficial, fully open-source co-op multiplayer plugin for **Space Engineers 2**. It
runs on Windows and Linux.

**Status: early development.** The protocol, transport and session layers are written and tested.
Game integration starts with Phase 0 in the plan. You can't play with it yet.

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
dotnet test tests/Tether.Core.Tests     # game-independent core and tests; no game needed
dotnet build src/Tether.Plugin           # plugin; needs a local Space Engineers 2 install
```

The plugin build looks for the game automatically:

- **Windows:** through Steam's registry entry.
- **Linux:** in `~/.steam/steam/steamapps/common/SpaceEngineers2/Game2`.

To point it somewhere else, copy `Directory.Build.props.user.example` to `Directory.Build.props.user`
and set `Game2Dir`.

## Repository layout

| Path | What it is |
|---|---|
| `src/Tether.Core` | Game-independent code: protocol, transports (loopback and LiteNetLib), host/client sessions |
| `src/Tether.Plugin` | The SE2 plugin: entry point, game adapter, and Steam transport (later) |
| `tests/Tether.Core.Tests` | Unit, fuzz and integration tests for the core |
| `tools/` | Install scripts |
| `docs/` | Plan and research notes |

## Legal

This is an unofficial community project, not affiliated with or endorsed by Keen Software House.
Space Engineers is a trademark of Keen Software House s.r.o. You need a legal copy of Space Engineers 2
to use this plugin.
