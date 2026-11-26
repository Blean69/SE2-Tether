# Tether test kit

Tether is an unofficial, open-source co-op multiplayer plugin for Space Engineers 2. The game already
contains multiplayer that Keen has built but hidden. This kit switches it on and adds the controls to use
it: open your world to friends, see your join code, and join a friend's world through Steam.

**This is an early test build.** Hosting works. This test finds out whether joining gets all the way
into the world. Expect rough edges, because the game's own multiplayer is unfinished.

- Tether sends no telemetry and writes only to its own folders.
- Use a new world, or back up the one you test with.
- To remove Tether, see [Uninstall](#uninstall).

Both players need Space Engineers 2 on the same game version, and the same Tether kit.

## Quick start (Windows)

1. **Unzip** this folder anywhere, for example your Desktop.
2. **Quit Space Engineers 2** if it's running, then double-click **`Install Tether.cmd`**.
   - Windows may warn that the file came from the internet. Choose "More info" > "Run anyway", or "Run".
   - If Windows says the file "was blocked because files of this type from the internet can be
     dangerous", with no way to run it, Smart App Control is on. See
     [Smart App Control](#smart-app-control-windows-11) below.
   - The installer is a short, readable script (`install.ps1`). It copies Tether's DLLs to
     `%LOCALAPPDATA%\Tether` and changes nothing else.
3. In Steam, right-click Space Engineers 2 > Properties > General > **Launch Options**, and press
   **Ctrl+V**. The installer already copied the right text to your clipboard.
4. **Start the game.** The main menu now has a **Multiplayer** button.

### Smart App Control (Windows 11)

Smart App Control is a Windows 11 security feature. When it's on, it does two things that affect Tether:

- It blocks downloaded scripts, such as `Install Tether.cmd`, with no option to run them anyway.
- It checks every DLL a program loads, and blocks unsigned ones that Microsoft's cloud service doesn't
  recognise. Tether's DLLs aren't code-signed yet, so the game won't be allowed to load Tether, however
  you install it.

There's no way to allow just one program. To check the setting, open **Windows Security > App & browser
control > Smart App Control settings**:

| Setting | What to do |
|---|---|
| **Off** | Nothing. |
| **Evaluation** | Nothing; evaluation mode blocks nothing. Windows may switch it to On later, and then Tether stops loading. |
| **On** | Turn it **Off** on that page to use this build. |

Before you turn it off:

- **Turning it back on:** on Windows 11 25H2 with current updates, you can turn it back on later from the
  same page. On older versions, turning it off is permanent unless you reset Windows.
- **Other protections stay:** Microsoft Defender Antivirus and SmartScreen keep protecting the PC.

Once it's off, run `Install Tether.cmd` again. The installer also warns if Smart App Control is on.
Code-signed builds would avoid all this, but they aren't available yet.

### Host a world

1. Load a world. After a few seconds a notification says multiplayer is ready. Your world starts
   **closed**.
2. Press **Ctrl+M**, or Esc > **Multiplayer**, to open the Multiplayer panel. Choose who can join:

   | Setting | Who can join |
   |---|---|
   | **Hidden** | Nobody. This is the default. |
   | **Private** | Only people you invite through Steam. |
   | **Friends** | Your Steam friends, from their friends list. |
   | **Public** | Anyone with your join code. |

3. Invite people: in Steam's friends list, right-click a friend > **Invite to game**. Or give them the
   **join code** the panel shows (Copy code puts it on your clipboard). The code only works when the world
   is set to Public.

While your world is open to others, or anyone is in it, the pause menu doesn't pause the game. Your game
runs the world for everyone, so pausing it would freeze them too.

### Join a friend's world

1. Start the game and wait at the main menu.
2. Do one of these:
   - In Steam's friends list, right-click the host > **Join Game**, or accept their invite.
   - Click **Multiplayer** in the main menu, type the host's join code, and click **Connect**. The host's
     world must be set to Public.

### After testing, report back

Quit the game, then send whoever gave you this kit:

- **`%APPDATA%\SpaceEngineers2\Tether\tether.log`.** Type that path into Explorer's address bar to find it.
- **The newest `SpaceEngineers2_*.log`** from `%APPDATA%\SpaceEngineers2\Temp\Logs`.
- **What you saw on screen:** loading, errors, or the world, and anything that looked wrong.

Both logs contain your Windows user name in some file paths, so look them over first.

## What's in the zip

| Path | What |
|---|---|
| `plugin/` | `Tether.dll` (entry point), `Tether.Runtime.dll`, `Tether.Core.dll`, `LiteNetLib.dll`, plus the multiplayer module: `Tether.Game.dll` and `0Harmony.dll` (the open-source Harmony patching library) |
| `nettest/`, `nettest.cmd`, `nettest.sh` | tether-nettest, a command-line network tester |
| `Install Tether.cmd`, `Uninstall Tether.cmd` | Double-click helpers for Windows. They run `install.ps1`. |
| `install.ps1`, `install.sh` | Installers for Windows and Linux/Proton. They copy files and show the launch option. |
| `SHA256SUMS` | Checksums of every file |
| `LICENSE`, `LICENSE-EXCEPTION.md` | GPL-3.0-or-later, plus the game-linking exception |
| `THIRD-PARTY-NOTICES.md` | The MIT licenses of LiteNetLib and Harmony |

## Verify the download (optional)

`SHA256SUMS` lists the checksum of every file. On Linux, run `sha256sum -c SHA256SUMS`.

Release kits are built with `tools/package.sh` on a machine with the game installed. The multiplayer
module, `Tether.Game.dll`, compiles against the game's own DLLs, which can't be redistributed or put in
CI. When a release is published, GitHub Actions runs `tools/verify-release.sh` on it (the "Verify
release" workflow):

- every other file is rebuilt from the tagged source and must match byte for byte;
- `0Harmony.dll` must be identical to the official Harmony package on nuget.org.

Anyone with the same game version can run that script at the release's tag to check `Tether.Game.dll`
as well.

## Settings

Add these to the launch options after the `-plugins:` option:

| Option | Effect |
|---|---|
| `-tether:debug` | Detailed log. The installer adds it for test builds. |
| `-tether:visibility=friends` | Open new worlds to friends straight away. Also `hidden`, `private`, `public`. |
| `-tether:mp=off` | Leave the game's multiplayer switched off. Tether then only runs its self-tests. |
| `-tether:safety=off` | Turn off the safety net, which logs failures in the game's multiplayer code and keeps the game running. With it off, they crash the game as usual. Useful for debugging. |

## Other tests

These check the foundations without multiplayer, and help narrow down problems.

### 1. Install by hand

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

- **Windows:** `-plugins:"C:\Users\<you>\AppData\Local\Tether\Tether.dll" -tether:debug`
- **Proton:** `'-plugins:C:\Tether\Tether.dll' -tether:debug`, including the single quotes. Steam on
  Linux passes launch options through a shell, which drops unquoted backslashes. The game would then
  report an incorrect plugin path.

### 2. Test A: the plugin loads

Start the game with the launch option above, load a world, then quit. Open Tether's log:

- **Windows:** `%APPDATA%\SpaceEngineers2\Tether\tether.log`
- **Proton:** `<steam library>/steamapps/compatdata/1133870/pfx/drive_c/users/steamuser/AppData/Roaming/SpaceEngineers2/Tether/tether.log`
- **Native Linux:** `~/.config/SpaceEngineers2/Tether/tether.log`

**Success** looks like this:

- a line saying `Tether bootstrap complete`;
- `Tether assemblies:` listing the DLLs from your install folder;
- `Multiplayer: on.` and the number of patch groups installed;
- `Lifecycle: OnBeforeProjectsLoaded` and `Lifecycle: OnBeforeEngineInstantiated`.

**If there's no `tether.log` at all**, the game didn't load the plugin:

1. Check the launch option.
2. Look for `tether-startup-error.log` in the same folder.
3. Look in the game's own log (`%APPDATA%\SpaceEngineers2\Temp\Logs`) for "plugin" errors.

### 3. Test B: in-game network self-test

This proves Tether's own networking can host from inside the running game. You need a second machine,
or the same machine, with `tether-nettest`.

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

### 4. Test C: two machines, no game

On machine A run `nettest.cmd host` (Windows) or `./nettest.sh host` (Linux). It prints addresses to
join. On machine B run `nettest join <A's address>`. Then chat, and use `/stats` and `/players`.

- **Same LAN:** this should just work.
- **Over the internet:** the host must forward UDP port 27027 on their router (or pick another with
  `--port`). Multiplayer through Steam doesn't need this; it's only for this test.

tether-nettest needs the .NET runtime 9 or newer. On Windows, Space Engineers 2 already installed it. On
Linux, install it from your distribution's packages or from https://dotnet.microsoft.com/download/dotnet.

When reporting on these tests, include the tether-nettest output (especially the `Link:` lines) and your
setup: Windows, Proton (and which version), or native Linux.

## Uninstall

1. Remove the `-plugins:` and `-tether:` launch options in Steam.
2. Double-click **`Uninstall Tether.cmd`** (Windows), or run `./install.sh --uninstall` (Linux), or just
   delete the install folder.
3. If you turned Smart App Control off for Tether, you can turn it back on where your Windows version
   allows it (see [Smart App Control](#smart-app-control-windows-11)).
