**Early test build.** This one targets the problem that stopped the first join test: the joining player
connected but never received the host's world.

Space Engineers 2 already contains multiplayer that Keen has built but hidden. Tether switches it on and
adds the controls to use it.

### What's new in alpha.2

- **Joining can now receive the host's world.** When someone joins, the host's game sends the whole world
  (tens of thousands of objects) in messages far larger than the 512 KB Steam allows for one message. The
  game handed them to Steam anyway, Steam refused them, and the game dropped them without a word, so the
  joining player got an empty world and then crashed. Tether now splits large messages into pieces and
  puts them back together on the other side.
- **A crash guard for joining players.** If the host removes an object the joining game never received,
  the joining game now skips it instead of crashing.
- **The pause menu's Multiplayer button now appears.** In alpha.1 it was added but drew nothing.

**Both players need alpha.2.** The host's game does the splitting and the joining game puts the pieces back
together.

### What it does

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
  `Install Tether.cmd`. It replaces an earlier Tether install. Paste the launch option into Steam (Space
  Engineers 2 > Properties > General > Launch Options); the installer copies it to your clipboard. If you
  already set it for alpha.1, it stays the same.
- **Linux with Proton:** run `./install.sh` from the unzipped folder, and set the launch option it
  prints, including its single quotes.

`README-TESTING.md` in the zip covers hosting, joining, and which logs to send back. After a join, the
host's `tether.log` should say "Network: sent a … MB message in … pieces", and the joining player's
"received a … MB message".

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

- **Joining is still being tested.** This fixes the problem the first test found. It's the first time the
  game's multiplayer gets this far, so there may be more behind it.
- **The first join can take a while:** the world is sent in one go, possibly tens of megabytes.
- **Windows 11 Smart App Control.** Tether isn't code-signed yet. If Smart App Control is on, Windows
  blocks `Install Tether.cmd` with no option to run it, and it won't let the game load Tether either.
  The only way around it is to turn Smart App Control off. Read
  [what that means](https://github.com/Blean69/SE2-Tether/blob/HEAD/docs/TESTING.md#smart-app-control-windows-11)
  before you do.
- **Join codes only work for Public worlds.** For friends, use Steam's Join Game or Invite to game.
- **Settings aren't saved.** Who-can-join resets to Hidden for each world, unless you add
  `-tether:visibility=friends` to the launch options.
- **The game's crash dialog blames Harmony** for any crash while Tether is loaded. That's the game's
  generic message about patching libraries, not a diagnosis. Send the logs instead.

Unofficial; not affiliated with Keen Software House. GPL-3.0-or-later with a game-linking exception.
Includes LiteNetLib and Harmony (MIT); see `THIRD-PARTY-NOTICES.md`.
