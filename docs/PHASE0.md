# Phase 0 runbook (first session on a machine with the game)

This turns the Phase 0 checklist in [`PLAN.md`](PLAN.md) §14 into concrete steps. The current plugin
is a **diagnostics-only** bootstrap. It changes nothing in the game. It logs what we need to know and
can dump the game's networking API names to local files.

It takes about 20 minutes. Do it on Windows first, then on Linux/Proton if you have it.

## 1. Build

```sh
git clone <this repo> && cd <repo>
dotnet test tests/Tether.Core.Tests        # sanity check: the core works without the game
dotnet build src/Tether.Plugin -c Release
```

The plugin build finds the game automatically: through Steam's registry entry on Windows, and in the
usual Steam paths on Linux. If it doesn't, copy `Directory.Build.props.user.example` to
`Directory.Build.props.user` and set `Game2Dir` to the folder that contains `SpaceEngineers2.exe`.

**If the build fails**, the most likely cause is that `IPlugin` or `PluginHost` live in a different
assembly than `VRage.Core.dll`. Note the error and add the right assembly to the reference list in
`src/Tether.Plugin/Tether.Plugin.csproj`. That's a Phase 0 finding in itself.

## 2. Install

| Platform | Command | Launch option it prints |
|---|---|---|
| Windows | `powershell -ExecutionPolicy Bypass -File tools\install.ps1` | `-plugins:"%LOCALAPPDATA%\Tether\Tether.dll"`, expanded to the real path |
| Linux (Proton) | `tools/install.sh` (run the game once under Proton first) | `-plugins:C:\Tether\Tether.dll` |

In Steam, open Space Engineers 2 > Properties > General > Launch Options and paste the printed option,
then add the diagnostic flags:

```
-plugins:"...\Tether.dll" -tether:debug -tether:dump-api
```

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
| Does `-plugins:` load us, and do our dependencies resolve? | `Tether bootstrap complete` means `Tether.Core.dll` resolved |
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
