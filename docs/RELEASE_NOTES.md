**Early test build.** Hosting works. Whether joining a friend's world gets all the way in is what this
build is here to find out.

Space Engineers 2 already contains multiplayer that Keen has built but hidden. Tether switches it on and
adds the controls to use it:

- **Multiplayer panel:** press Ctrl+M in a world, or Esc > Multiplayer. It shows whether you're hosting,
  your join code, and who can join.
- **Who can join:** Hidden, Private (Steam invites only), Friends, or Public (anyone with the code).
  Worlds start closed until you open them.
- **Joining:** through Steam (right-click a friend > Join Game, or accept their invite), or with a join
  code from the main menu's Multiplayer button.
- **Notifications** in the game when multiplayer is ready, when players join, and when a connection fails.

Tether sends no telemetry and writes only to its own folders. It never modifies game files.

### Install

Both players need Space Engineers 2 **4.0.0.3169** (the current Steam build) and this same kit.

- **Windows:** download the zip below and unzip it. Quit the game, then double-click
  `Install Tether.cmd`. Paste the launch option into Steam (Space Engineers 2 > Properties > General >
  Launch Options); the installer copies it to your clipboard.
- **Linux with Proton:** run `./install.sh` from the unzipped folder, and set the launch option it
  prints, including its single quotes.

`README-TESTING.md` in the zip covers hosting, joining, and which logs to send back.

### Verify what you downloaded

- `SHA256SUMS` inside the zip lists every file's checksum. The `.zip.sha256` file below is the zip's own.
- This kit was built with `tools/package.sh` at this release's tag, on a machine with the game installed.
  The multiplayer module (`Tether.Game.dll`) compiles against the game's own DLLs, which can't go into CI.
- The **Verify release** workflow (Actions tab) rebuilds every other file from the tagged source and
  checks it matches byte for byte. It also checks that `0Harmony.dll` is the official Harmony package
  from nuget.org.
- With the same game version installed, `tools/verify-release.sh <zip>` at this tag rebuilds and checks
  `Tether.Game.dll` too.

### Known limitations

- **Windows 11 Smart App Control.** Tether isn't code-signed yet. If Smart App Control is on, Windows
  blocks `Install Tether.cmd` with no option to run it, and it won't let the game load Tether either.
  The only way around it is to turn Smart App Control off. Read
  [what that means](https://github.com/Blean69/SE2-Tether/blob/HEAD/docs/TESTING.md#smart-app-control-windows-11)
  before you do.
- **Joining is untested end to end.** The game's own multiplayer is unfinished, so expect rough edges.
- **Join codes only work for Public worlds.** For friends, use Steam's Join Game or Invite to game.
- **First native Windows run.** If the game reports an incorrect plugin path, reinstall to a simple
  folder: run `powershell -ExecutionPolicy Bypass -File .\install.ps1 -Destination C:\Tether`, then
  use the launch options `-plugins:C:\Tether\Tether.dll -tether:debug`.
- **Settings aren't saved.** Who-can-join resets to Hidden for each world, unless you add
  `-tether:visibility=friends` to the launch options.

Unofficial; not affiliated with Keen Software House. GPL-3.0-or-later with a game-linking exception.
Includes LiteNetLib and Harmony (MIT); see `THIRD-PARTY-NOTICES.md`.
