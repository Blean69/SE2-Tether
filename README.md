# Tether

**Tether** is an unofficial, fully open-source co-op multiplayer plugin for **Space Engineers 2**. It
runs on Windows and Linux.

**Status: early test build.** Space Engineers 2 already contains multiplayer that Keen has built but
hidden. Tether switches it on and adds the controls to use it:

- **Multiplayer panel:** press Ctrl+M in a world, or Esc > Multiplayer. It shows whether you're hosting
  and your join code.
- **Who can join:** Hidden, Private (Steam invites only), Friends, or Public (anyone with the code).
  Worlds start closed until you open them.
- **Joining:** through Steam (right-click a friend > Join Game, or Invite to game), or with a join code
  from the main menu's Multiplayer button.
- **Notifications** in the game when multiplayer is ready, when players join, and when a connection fails.

Hosting works. Whether joining gets all the way into the world is what the current test is finding out.

## Download and install

Get the latest zip from [Releases](https://github.com/Blean69/SE2-Tether/releases). Both players need
the same game version and the same Tether kit.

- **Windows:** unzip, quit the game, and double-click `Install Tether.cmd`. Then paste the launch option
  into Steam (Space Engineers 2 > Properties > General > Launch Options). The installer copies it to
  your clipboard.
- **Linux with Proton:** run `./install.sh` from the unzipped folder and set the launch option it prints.

**Windows 11 Smart App Control:** Tether's DLLs aren't code-signed yet, so with Smart App Control on,
Windows blocks the installer and won't let the game load Tether. If Windows blocks `Install Tether.cmd`
with no option to run it, read
[Smart App Control](docs/TESTING.md#smart-app-control-windows-11) first.

`README-TESTING.md` in the zip covers hosting, joining, and what to send back. It's the same file as
[`docs/TESTING.md`](docs/TESTING.md). To remove Tether, run `Uninstall Tether.cmd`
(or `./install.sh --uninstall`) and clear the launch options.

## How it works

The game ships a session system behind a hidden feature switch. It runs on Steam lobbies and Steam
peer-to-peer networking, with join codes. Tether:

1. turns that switch on when the game starts;
2. keeps hosted worlds closed until the host chooses who can join;
3. adds the in-game panel, the pause-menu button, and the notifications.

It patches the game in memory with [Harmony](https://github.com/pardeike/Harmony). It never modifies
game files.

Tether also has its own network stack (`Tether.Core`), tested and working inside the game. It's there
for diagnostics and as a fallback if the built-in path falls short.

- Design plan: [`docs/PLAN.md`](docs/PLAN.md)
- Research notes: [`docs/research/2026-11-landscape.md`](docs/research/2026-11-landscape.md)
- First run with the game (Phase 0): [`docs/PHASE0.md`](docs/PHASE0.md)

## What v1 aims at

Small co-op sessions of up to 4 players:

- One player hosts from inside the game. Their game is the authority.
- Friends join through Steam, using Valve's relay network. That means no port forwarding and no
  dedicated server.
- Join codes for people who aren't Steam friends. Direct IP later, for LAN play.

## Principles

- **All source is public**, under GPL-3.0-or-later with a narrow game-linking exception (see
  [`LICENSE-EXCEPTION.md`](LICENSE-EXCEPTION.md)).
- Builds are reproducible, and every release is checked against the source (see
  [Verifying a release](#verifying-a-release)).
- No account gates and no telemetry.
- No closed executables. Install scripts are short and readable.
- Windows and Linux are equals:
  - Windows;
  - Linux via Proton;
  - native Linux via Pulsar and LinuxCompat.
- We never redistribute Keen Software House files.

## Verifying a release

Each kit has a `SHA256SUMS` file. The `.zip.sha256` file next to it on the release page is the zip's own
checksum.

Release kits are built with `tools/package.sh` on a machine with the game installed. The multiplayer
module, `Tether.Game.dll`, compiles against the game's own DLLs, which can't be redistributed or put in
CI. When a release is published, the [Verify release](.github/workflows/verify-release.yml) workflow
runs [`tools/verify-release.sh`](tools/verify-release.sh) on it:

- every other file is rebuilt from the tagged source and must match byte for byte;
- `0Harmony.dll` must be identical to the official Harmony package on nuget.org.

The results are on the workflow run's summary page. Anyone with the same game version can run the
script at the release's tag to check `Tether.Game.dll` as well:

```sh
git checkout v0.1.0-alpha.1
tools/verify-release.sh path/to/tether-0.1.0-alpha.1.zip
```

## Building

You need the .NET 10 SDK. The plugin targets `net9.0`, the runtime the game uses, and the tests also
need the .NET 9 runtime.

```sh
dotnet test tests/Tether.Core.Tests     # core tests; no game needed
tools/package.sh                        # the kit zip
```

- **Without the game,** `package.sh` builds everything except the multiplayer module. Each CI run
  produces such a kit (`tether-test-kit`). It runs the diagnostics and network tests only.
- **With the game installed,** it also builds `Tether.Game.dll`. `TETHER_REQUIRE_GAME=1` makes it fail
  instead of leaving the module out.

Where the build looks for the game:

- **Windows:** found through Steam's registry entry.
- **Linux:** found in `~/.steam/steam/steamapps/common/SpaceEngineers2/Game2`.

To point the build somewhere else, copy `Directory.Build.props.user.example` to
`Directory.Build.props.user` and set `Game2Dir`.

## Repository layout

| Path | What it is |
|---|---|
| `src/Tether.Core` | Game-independent code: protocol, transports (loopback and LiteNetLib), host/client sessions, network-test node |
| `src/Tether.Plugin` | `Tether.dll`, the tiny entry point the game loads (no dependencies besides the game's `IPlugin`) |
| `src/Tether.Runtime` | Tether's runtime in the game process: settings, logging, diagnostics, network self-test. Loads the game module if present. |
| `src/Tether.Game` | The multiplayer module: switches on the game's multiplayer, controls who can join, and adds the in-game UI. Compiles against the game. |
| `src/Tether.NetTest` | `tether-nettest`, a command-line network tester (no game needed) |
| `ref/VRage.Core.Interop` | Hand-written declarations of the two game types the plugin compiles against. Not Keen code; never shipped. |
| `tests/` | Unit, fuzz and integration tests; a stand-in game process for end-to-end tests |
| `tools/` | Install, package (`package.sh`), release verification (`verify-release.sh`) and end-to-end test scripts |
| `docs/` | Plan, research notes, Phase 0 runbook, testing guide |

## Legal

This is an unofficial community project, not affiliated with or endorsed by Keen Software House.
Space Engineers is a trademark of Keen Software House s.r.o. You need a legal copy of Space Engineers 2
to use this plugin.

Release kits include [LiteNetLib](https://github.com/RevenantX/LiteNetLib) and
[Harmony](https://github.com/pardeike/Harmony), both MIT-licensed; see
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).
