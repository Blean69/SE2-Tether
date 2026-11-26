# Tether test kit (Phase 0)

Tether is an unofficial, open-source co-op multiplayer plugin for Space Engineers 2. This kit is the
**first testable build**. It doesn't add multiplayer to the game yet. It checks the three things
everything else depends on:

1. **The plugin loads** in the real game: on Windows, under Proton, and on native Linux.
2. **Tether's networking works inside the game process**, which proves the in-game network self-test
   can reach another machine.
3. **Two machines can reach each other** with Tether's network stack (`tether-nettest`, no game needed).

It changes nothing in your game or saves. To remove it, delete the launch option and the install folder.

## What's in the zip

| Path | What |
|---|---|
| `plugin/` | Tether.dll (entry point), Tether.Runtime.dll, Tether.Core.dll, LiteNetLib.dll |
| `nettest/`, `nettest.cmd`, `nettest.sh` | tether-nettest, a command-line network tester |
| `install.ps1`, `install.sh` | Installers for Windows and Linux/Proton. They copy files and print the launch option. |
| `SHA256SUMS` | Checksums of every file |
| `LICENSE`, `LICENSE-EXCEPTION.md` | GPL-3.0-or-later, plus the game-linking exception |

## 0. Verify the download (optional, recommended)

This zip is built by GitHub Actions from the public source. To check it:

```sh
sha256sum -c SHA256SUMS                                   # Linux
gh attestation verify tether-*.zip --repo Blean69/SE2-Tether  # proves which workflow and commit built it
```

Attestations are produced once the repository is public.

You can also rebuild it yourself: run `tools/package.sh` at the same commit. The DLLs and the zip should
come out byte-identical.

## 1. Install the plugin

**Windows**, in PowerShell, from the unzipped folder:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

**Linux with Proton.** Start the game once under Proton first, then:

```sh
./install.sh
```

Each installer prints the launch option to paste into Steam (Space Engineers 2 > Properties > General >
Launch Options):

- **Windows:** `-plugins:"C:\Users\<you>\AppData\Local\Tether\Tether.dll"`
- **Proton:** `-plugins:C:\Tether\Tether.dll`

## 2. Test A: the plugin loads

Add `-tether:debug` after the `-plugins:` option, start the game, load a world, then quit. Open Tether's log:

- **Windows:** `%APPDATA%\SpaceEngineers2\Tether\tether.log`
- **Proton:** `<steam library>/steamapps/compatdata/1133870/pfx/drive_c/users/steamuser/AppData/Roaming/SpaceEngineers2/Tether/tether.log`
- **Native Linux:** `~/.config/SpaceEngineers2/Tether/tether.log`

**Success** looks like this:

- a line saying `Tether bootstrap complete`;
- `Tether assemblies:` listing all four DLLs from your install folder;
- `Lifecycle: OnBeforeProjectsLoaded` and `Lifecycle: OnBeforeEngineInstantiated`.

**If there's no `tether.log` at all**, the game didn't load the plugin:

1. Check the launch option.
2. Look for `tether-startup-error.log` in the same folder.
3. Look in the game's own log (`%APPDATA%\SpaceEngineers2\Temp\Logs` or similar) for "plugin" errors.

## 3. Test B: in-game network self-test

This proves Tether can host a session from inside the running game. You need a second machine, or the
same machine, with `tether-nettest`.

1. Add `-tether:host-test` to the game's launch options. Optionally also add `-tether:name=YourName` and
   `-tether:password=secret`.
2. Start the game.
   - On Windows, a firewall prompt may appear for SpaceEngineers2.exe. Allow it on private networks.
     Run the game windowed if the prompt hides behind it.
3. On the other machine, or a terminal on the same one, join:

   ```sh
   nettest.cmd join <game PC's IP address>     # Windows
   ./nettest.sh join <game PC's IP address>    # Linux
   ```

   - Use `127.0.0.1` if it's the same machine.
   - Add `--password secret` if you set one.

**Success** has three signs:

- tether-nettest prints `Welcome, <name>! This host is running inside Space Engineers 2 (...)`.
- Typing a line in tether-nettest shows it in the game's `tether.log`.
- Every 10 seconds both sides report round-trip time and probe loss.

**Reverse direction:** run `nettest host` on the other machine and start the game with
`-tether:join-test=<that machine's IP>`.

## 4. Test C: two machines, no game

On machine A run `nettest.cmd host` (Windows) or `./nettest.sh host` (Linux). It prints addresses to
join. On machine B run `nettest join <A's address>`. Then chat, and use `/stats` and `/players`.

- **Same LAN:** this should just work.
- **Over the internet:** the host must forward UDP port 27027 on their router (or pick another with
  `--port`). The finished Tether will use Steam's relay for friends, so port forwarding is only needed
  for this test and for direct-IP play.

tether-nettest needs the .NET runtime 9 or newer. On Windows, Space Engineers 2 already installed it. On
Linux, install it from your distribution's packages or from https://dotnet.microsoft.com/download/dotnet.

## 5. Report back

These help most:

- **`tether.log`** from Test A and Test B. It contains local paths, such as your username, so review it
  before sharing.
- **The tether-nettest output** from Tests B and C, especially the `Link:` lines.
- **Your setup:** Windows, Proton (and which version), or native Linux.

## Uninstall

1. Remove the `-plugins:` and `-tether:` launch options in Steam.
2. Run the installer again with `-Uninstall` (Windows) or `--uninstall` (Linux), or just delete the
   install folder.
