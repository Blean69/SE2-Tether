# Phase 0 runbook (first session on a machine with the game)

This turns the Phase 0 checklist in [`PLAN.md`](PLAN.md) §14 into concrete steps. The current plugin
is a **diagnostics-only** bootstrap. It changes nothing in the game. It logs what we need to know and
can dump the game's networking API names to local files.

It takes about 20 minutes. Do it on Windows first, then on Linux/Proton if you have it.

## 1. Get Tether

**Easiest: download the test kit.** No .NET SDK is needed. Each CI run on GitHub (Actions > CI > the
latest run) attaches a `tether-test-kit` artifact. Unzip it; `README-TESTING.md` inside covers the
install and the network tests in detail.

**Or build from source** with the .NET 10 SDK:

```sh
tools/package.sh            # builds artifacts/tether-<version>.zip, byte-identical to CI's for the same commit
# or, to compile against your own game install for stronger compile-time checks:
dotnet build src/Tether.Plugin -c Release
```

The plugin build finds the game automatically: through Steam's registry entry on Windows, and in the
usual Steam paths on Linux. To point it elsewhere, set `Game2Dir` in `Directory.Build.props.user` (see
the `.example` file).

**If a build against the real game fails**, the likely cause is that `IPlugin` or `PluginHost` live
somewhere other than `VRage.Core.dll`. That's a Phase 0 finding: note the error. The test kit build uses
Tether's own interop declarations instead (`ref/VRage.Core.Interop`), and Test A shows whether those
match the game.

## 2. Install

From the unzipped kit (or `tools/` in the repo):

| Platform | Command | Launch option it prints |
|---|---|---|
| Windows | `powershell -ExecutionPolicy Bypass -File install.ps1` | `-plugins:"%LOCALAPPDATA%\Tether\Tether.dll"`, expanded to the real path |
| Linux (Proton) | `./install.sh` (run the game once under Proton first) | `-plugins:C:\Tether\Tether.dll` |

In Steam, open Space Engineers 2 > Properties > General > Launch Options and paste the printed option,
then add the diagnostic flags:

```
-plugins:"...\Tether.dll" -tether:debug -tether:dump-api
```

To also run the in-game network self-test in the same session, add `-tether:host-test` and join from
another machine with `nettest join <IP>` (see `README-TESTING.md`, Test B).

## 3. Run

1. Start the game and wait for the main menu.
2. Load any single-player world and walk around for a minute.
3. Quit normally.

## 4. Collect

Everything is in Tether's data folder:

- **Windows:** `%APPDATA%\SpaceEngineers2\Tether\`
- **Proton:** `<steam library>/steamapps/compatdata/1133870/pfx/drive_c/users/steamuser/AppData/Roaming/SpaceEngineers2/Tether/`
- **Native Linux:** `~/.config/SpaceEngineers2/Tether/`

| File | What it is | Share? |
|---|---|---|
| `tether.log` | Environment, game build, runtime config, PluginHost API, lifecycle timing | Yes. It contains local paths (your username), so review it first. |
| `tether-startup-error.log` | Only exists if Tether failed to start | Yes |
| `api-dumps/*.txt` | Names and signatures of Keen's networking types | Keep them private. Use them locally to write `docs/engine-map.md` in your own words. |

If `tether.log` doesn't exist at all, the game didn't load the plugin. Check the launch option and
look in the game's own log for "Tether" or plugin errors.

## 5. What the log answers

| Phase 0 question | Where to look in `tether.log` |
|---|---|
| Runtime version, and whether startup hooks are allowed | `SpaceEngineers2.runtimeconfig.json:` and `Startup hooks switch:` |
| Game build to pin | `SpaceEngineers2.dll file version:` |
| Does `-plugins:` load us, and do our dependencies resolve? | `Tether bootstrap complete`, and `Tether assemblies:` lists all four DLLs from the install folder |
| PluginHost API in this build | the `PluginHost (...)` line |
| Lifecycle timing | the `Lifecycle:` lines |
| Proton detection | `Platform:` |
| Is Steamworks loaded early? | `Loaded assemblies: ...; Steamworks:` |

## 6. Manual experiments (optional, same session)

1. **Two instances on one PC.** Add `-allowMultiple` and start the game twice. Does the second instance
   run? This decides whether we can test the direct-UDP transport on one machine.
2. **Keen's hidden multiplayer.** Start once with `-enableMultiplayer`, **offline or on LAN only**, and
   note what appears: a Players tab, host options, any join UI. This is only to learn Keen's design; we
   don't use Keen's online services.
3. **Diff after a game update.** Keep the `api-dumps` folder from each game build. Diffing two dumps
   shows exactly what Keen changed in the networking code.

## 7. Report back

Bring back the reviewed `tether.log`, the answers to section 6, and your own-words notes on what the
dumps show. Those notes go into `docs/engine-map.md`. Then Phase 1 can start: the engine adapter that
connects Keen's replication to Tether's transport.
