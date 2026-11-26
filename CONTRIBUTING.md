# Contributing to Tether

Thanks for helping. Tether exists so that players can see exactly what they run. Contributions have to
keep it that way.

## Ground rules

1. **No Keen code in this repository.**
   - Never commit decompiled game code, game DLLs, reference stubs, or API dumps. The `-tether:dump-api`
     output is git-ignored for this reason.
   - Describe what you learn about the game in your own words in `docs/`. Only the type and member names
     needed for interop belong in our code.
2. **No copied code from incompatible sources.**
   - SE2 Online's source is under a custom non-commercial license that isn't GPL-compatible.
   - The Space Engineers 1 source is under an EULA that limits its use to SE1.
   - Reading either to understand an approach is fine, but write your own implementation.
3. **No binaries in git.** CI rejects them. Releases are built from source.
4. **No telemetry, auto-update or phone-home.** If a feature needs the network beyond the game session
   itself, it has to be opt-in and documented.
5. **Treat network input as hostile.**
   - Decoders must be bounds-checked and limit their sizes.
   - Decoders may only throw `ProtocolViolationException`.
   - Add fuzz-test seeds for every new message.
6. **Keep the core game-free.** `src/Tether.Core` must never reference game assemblies, so it can build
   and test on any machine and in CI.

## Building

- **SDK:** the .NET 10 SDK (pinned in `global.json`). The tests need the .NET 9 runtime.
- **Core and tests:** `dotnet test tests/Tether.Core.Tests`. No game is needed.
- **Plugin:** `dotnet build src/Tether.Plugin -c Release`. It needs a local Space Engineers 2 install;
  see `Directory.Build.props.user.example`.

## Protocol changes

Bump `ProtocolConstants.ProtocolVersion` for **any** wire change. Never renumber `MessageType` or
`DisconnectReason` values; only add new ones. The start of `Hello` (type byte plus protocol version) is
frozen forever.

## Code style

- File-scoped namespaces.
- Explicit `using`s, with no implicit usings. Pulsar compiles the plugin without MSBuild, so this matters.
- Every source file starts with the SPDX header:

  ```csharp
  // SPDX-License-Identifier: GPL-3.0-or-later
  // Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.
  ```

By contributing, you agree that your contribution is licensed under GPL-3.0-or-later with the
game-linking exception in `LICENSE-EXCEPTION.md`.
