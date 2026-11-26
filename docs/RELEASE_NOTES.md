**Test build. This does not add multiplayer to the game yet.**

This is Tether's first tester kit: an open-source co-op multiplayer plugin for Space Engineers 2, at an
early stage. This build checks the foundations on your machine:

- **The plugin loads in your game.** It's verified on Linux with Proton; native Windows is what we want
  to confirm next.
- **Tether's networking works from inside the game process.** The in-game network self-test lets
  another Tether user connect to your game, or your game connect to theirs.
- **Two machines can reach each other** with `tether-nettest`, without the game.

It makes no gameplay changes, sends no telemetry, and writes only to its own folders.

### Install (Windows)

Download the zip below, unzip it, quit the game, and double-click `Install Tether.cmd`. Then paste the
launch option into Steam; the installer copies it to your clipboard. Full steps and tests are in
`README-TESTING.md` inside the zip. Linux/Proton users run `./install.sh` instead.

### Verify what you downloaded

- `SHA256SUMS` inside the zip lists every file's checksum.
- The `.zip.sha256` file next to the zip is the zip's own checksum.
- This release is built by GitHub Actions from the tagged commit. Rebuilding with `tools/package.sh` at
  the same commit gives a byte-identical zip.
- When the repository is public, `gh attestation verify <zip> --repo Blean69/SE2-Tether` proves which
  workflow and commit built it.

### Known limitations

- No gameplay sync yet. That's the next phase.
- Internet tests need the host to forward UDP port 27027. Steam P2P, which needs no port forwarding, is
  planned.
- Test chat appears in `tether.log` and in `tether-nettest`, not in the game's chat.

Unofficial; not affiliated with Keen Software House. GPL-3.0-or-later with a game-linking exception.
