# Tether design plan

**Status:** v0.2 (2026-11-26). The project is named **Tether**, licensed GPL-3.0-or-later with a
game-linking exception, and v1 is scoped to small co-op sessions (§1). Implementation of the
game-independent core has started.
**Evidence:** [`docs/research/2026-11-landscape.md`](research/2026-11-landscape.md).
**Markers:** **[DECIDE]** is a decision for the maintainer. **[P0]** is something to verify on a machine
with the game installed (see the Phase 0 checklist in §14).

---

## 1. Goals and non-goals

**Goals**

1. Multiplayer for Space Engineers 2: host from inside the game, join friends, and later run dedicated
   servers.
2. **Fully open source** under an OSI-approved license. Every binary a player runs must be traceable to
   source in this repo and reproducible from it.
3. **Windows and Linux as equals**: Linux under Proton, native Linux via LinuxCompat, and Linux
   dedicated servers.
4. **Nothing hidden**:
   - no account gates and no "sign in with Steam to download";
   - no telemetry;
   - no auto-updater by default;
   - no mandatory third-party services;
   - no closed executables, and installers are plain readable scripts.
5. Respect Keen's terms where we can (see §16) and converge with Keen's official multiplayer rather than
   fight it.

**Non-goals (for now)**

- Anything commercial. Keen's EULA forbids commercial mods, and we want none anyway.
- Redistributing any Keen file: DLLs, stubs, runtime, or the dedicated-server payload.
- Anti-cheat beyond server authority and input validation.
- Compatibility with other multiplayer mods. We will detect them and refuse to run alongside them.

### v1 scope (decided 2026-11-26)

- **Small co-op: up to 4 players**, the host included. This is a protocol default, not a hard limit.
- **Listen server only.** One player hosts from inside the game, and their game is the authority. In
  networking terms this is a star: every client connects to the host. No dedicated servers in v1.
- **"Peer-to-peer" means the connections, not the authority.**
  - Players connect directly to each other through Steam's P2P networking, relayed through Valve's
    network, so there is no port forwarding and no server to rent.
  - There is no full mesh where each player simulates their own objects. Keen's replication is
    server-authoritative, and full mesh would mean desync and cheating problems with Havok physics.
- **Transports:**
  - **Steam P2P** is the default for playing with friends.
  - **Direct UDP (LiteNetLib)** covers LAN play and development, and lets you run two game instances
    on one PC.
- **Out of scope for v1:**
  - dedicated or headless servers;
  - a public server browser;
  - voice chat;
  - remote admin;
  - more than about 8 players.

The design below still keeps dedicated servers possible later. Nothing in the protocol or session
layer assumes the host is also a player.

## 2. Where we fit

| | Keen official | SE2 Online | SE2MP (se2mp.com) | **This project** |
|---|---|---|---|---|
| Available | Early 2027 (4-player co-op) | Now | Now | — |
| Source | Closed | Public, custom non-OSI license | Closed | OSI license, public |
| Download | Steam | Website needs Steam sign-in; GitHub and PluginHub don't | Discord only | GitHub and PluginHub, no gate |
| Dedicated server | "Later" | Headless game; ships Keen files | Headless "DsProto" | Headless game, never ships Keen files |
| Linux | Unsupported | Not a focus | Unknown | First-class |
| Telemetry | ? | Opt-in server list | Auto log and crash upload | None |

**Strategy:**

- Keen ships 4-player co-op in early 2027, and the 2026-11-30 "Special Announcement" may change the
  picture again. Our durable value is everything Keen won't do soon or ever:
  - open source;
  - Linux;
  - more than 4 players;
  - dedicated servers before Keen ships them, and Linux ones after;
  - admin tooling.
- The architecture below builds on Keen's own replication code. That way, as official multiplayer
  matures, the project shrinks into an extension layer (in the spirit of SE1's Torch) instead of a
  competing netcode.

## 3. Facts we build on

Each fact is detailed with sources in the research notes.

- **Plugin entry point:**
  - The stock game loads plugins with the launch option `-plugins:<path.dll>`.
  - Our class implements `Keen.VRage.Core.Plugins.IPlugin`, and its constructor takes a `PluginHost`.
  - **No injector, proxy DLL or startup hook is required.**
- **Runtime:** the stock game runs on .NET 9 and Pulsar runs it on .NET 10, so we target **`net9.0`**
  [P0: confirm `SpaceEngineers2.runtimeconfig.json`].
- **Keen's netcode already exists in-process:**
  - VRAGE3 is split into server and client code.
  - Single-player already runs a server in-process and replicates to the local client.
  - Keen's replication layer (`Keen.VRage.Multiplayer.*`) and an "OutOfProcess" peer transport ship in
    the game.
  - Keen's own unfinished multiplayer sits behind the `-enableMultiplayer` flag.
- **Physics:** Havok (native) is the physics engine, and simulation is not deterministic. State sync is
  therefore the only realistic model; lockstep is out.
- **Steam:** Steam is initialised inside the game under App ID 1133870, and the game ships
  Steamworks.NET.
- **Game binaries:** they are not obfuscated. They are ReadyToRun-compiled, so beware of inlining when
  patching.

## 4. Core decision: whose netcode?

| Option | Description | Pros | Cons |
|---|---|---|---|
| **A. Keen's replication, our pipes** | Keep Keen's replication and simulation. Replace the transport and session layer with ours. Patch the gaps feature by feature. | Reuses the server/client split Keen designed. Every block, inventory and system Keen replicates comes for free. Converges with official multiplayer. Proven viable: SE2 Online works this way. | Coupled to internals Keen is actively changing (expect breakage on updates). The gap-filling work is still large: SE2 Online needed about 80 feature patches. |
| **B. Our own sync (Nitrox/LMP style)** | Our own entity model, ownership locks and packets for everything. | Independent of Keen's unfinished netcode. | We would re-implement sync for hundreds of block types, Havok physics, voxels and inventories. Not feasible for a community team, and it diverges from Keen. |
| **C. Turn on Keen's hidden multiplayer** | Use `-enableMultiplayer` as-is and patch whatever is missing. | The thinnest possible plugin. | Unfinished and undocumented. May depend on Keen or Steam session services we must not lean on (EULA §3 "interference"). Keen can change it at any time. |

**Recommendation: A.**

- We still study C in Phase 0, purely to learn what Keen's own transport expects, and we never run
  against Keen's online services.
- Where Keen's replication has holes (for example, voxel edits for late joiners), we fill them with our
  own messages on a side channel, using B-style techniques only locally.

## 5. Architecture overview

```
 ┌──────────────── Space Engineers 2 process (host, client, or headless server) ───────────────┐
 │                                                                                              │
 │  Keen game code (Game2.*, VRage.*)                                                           │
 │    Simulation (DCS + Havok, server side)  <->  Replication layer  <->  Transport slot        │
 │                                                                          ^                   │
 │ ......................................................................  | thin adapter ..... │
 │  Tether plugin                                                           v                   │
 │    Bootstrap ── build check ── Harmony patches (Engine adapter + Feature modules)            │
 │    Session: host/join, players, permissions, mod check, world transfer                       │
 │    Protocol: our control messages (versioned, hand-written, bounds-checked)                  │
 │    Transport abstraction ──┬── Steam backend  (SteamNetworkingSockets + relay, lobbies)      │
 │                            └── Direct backend (LiteNetLib UDP: IP / LAN / dedicated)         │
 └──────────────────────────────────────────────────────────────────────────────────────────────┘
```

### Components

1. **Bootstrap** (`IPlugin`):
   - Reads our launch args.
   - Checks the game build against the supported list (§12).
   - Detects conflicting multiplayer plugins.
   - Installs Harmony patches early, in `PluginHost` callbacks before the engine is built.
   - Under Pulsar, registers with `MetadataManager`.
2. **Engine adapter:**
   - **The only code that names Keen networking types**, kept small on purpose.
   - Bridges Keen's replication byte streams to our transport, in both directions.
   - Starts and stops Keen's server/client replication components for a session.
   - [P0] Decide whether to implement Keen's transport interface directly (cleanest), if one exists, or
     to intercept the OutOfProcess components' per-frame jobs.
3. **Feature modules:**
   - Each gap fix lives in its own module with `Install()`/`Uninstall()`, wrapped so that a failure
     disables only that module, logs it, and shows it in the UI.
   - Expected areas: join/spawn, world transfer, voxels, chat, ownership/factions, admin, and UI.
     Phase 0/1 will tell us the real list.
4. **Session:**
   - Lifecycle: host, join, leave, kick, ban.
   - Player registry and identities.
   - Permissions (admin and player, by SteamID or by public key on non-Steam servers).
   - Mod-list match and world transfer.
5. **Protocol:** our own control messages (§7.3). Keen's replication payloads pass through opaquely.
6. **Transport abstraction:**
   - Interface: `ITransport` / `IConnection`, with channels for reliable-ordered, reliable-unordered and
     unreliable-sequenced traffic.
   - Handles fragmentation, MTU, bandwidth caps, stats and network-condition simulation.
   - Two backends (§7), plus a loopback backend for tests.
7. **UI:** a Multiplayer screen (host, join by IP, join friend or invite, settings) built with the game's
   own UI framework (Avalonia), plus an in-game players/admin panel.
8. **Headless server:** the same plugin running the game headless with rendering off. Keen's
   `HeadlessEngine` and `GameSessionDedicatedServerComponent` exist [P0]. See §8.

### Code boundaries (enforced in CI)

- **`Tether.Core`** has **no game references**: protocol, transport abstraction and backends, session
  logic, config and serialization. It builds and tests anywhere, on Linux and Windows CI.
- **`Tether.Plugin`** references game assemblies and Harmony. It holds the bootstrap, engine adapter,
  feature modules and UI.
- The Steam backend lives in `Plugin`, not `Core`. It needs the game's own `Steamworks.NET.dll`, which
  we must reference but never ship.

## 6. Loading, install and uninstall

Two supported ways in, from one codebase and one DLL:

1. **Stock game: `-plugins:`**
   - Install means copying a folder and adding a launch option. Nothing else.
   - Installers are short, readable scripts: `install.ps1` (Windows) and `install.sh` (Linux/Proton).
   - The scripts do three things:
     - find the Steam library through `libraryfolders.vdf`;
     - copy the files;
     - **print** the launch option for the user to paste.
   - Editing Steam's `localconfig.vdf` behind the user's back is opt-in only, and never while Steam is
     running.
2. **Pulsar PluginHub**: the most trustworthy channel. Pulsar compiles our pinned commit **from source
   on the player's own machine**. Requirements this places on our code:
   - Code must compile under Pulsar's Roslyn without MSBuild magic: an explicit `GlobalUsings.cs`, no
     source generators, and NuGet references declared in the hub XML.
   - Avoid or stub the `VRage.Library.Generator` duplicate-type trap.

Rejected loading methods:

- DLL proxies (`winhttp.dll` and similar): antivirus flags, opaque, need Wine DLL overrides.
- Replacing game assemblies.
- A custom closed launcher.

Fallbacks, used only if Keen ever removes `-plugins:`:

- `DOTNET_STARTUP_HOOKS`, which is generic .NET and needs no native code;
- a tiny open-source launcher.

Uninstalling means removing the launch option and deleting our folder. The plugin writes only to its
own folder and its own config/log directory.

## 7. Networking

### 7.1 Steam backend (default for "play with friends")

- **Library and transport:**
  - Uses the **game's** Steamworks.NET instance; Steam is already initialised under 1133870.
  - Never re-initialises Steam, and never uses App ID 480. Both break the game's Steam features.
  - Transport is `ISteamNetworkingSockets` P2P over Valve's relay (SDR). That gives NAT and CGNAT
    traversal, encryption and SteamID-authenticated peers at no cost, with no port forwarding.
- **Discovery:** Steam lobbies and rich presence for friend join and invites.
  - Lobbies carry a namespaced metadata key and our protocol version, so they never collide with Keen's
    own sessions.
  - [P0] Find out how Keen's lobbies are tagged.

### 7.2 Direct backend (LAN, direct IP, dedicated servers)

- **LiteNetLib (MIT)** over UDP, with channels, fragmentation and MTU discovery. It is pure C# and works
  on Windows, Linux and Proton.
- Port forwarding or UPnP is needed for home hosting. That's acceptable because Steam covers the
  easy case.
- **Identity:**
  - When both ends run Steam, use **Steam auth session tickets** (`GetAuthSessionTicket` and
    `BeginAuthSession` on the host).
  - Otherwise, a per-player keypair (ECDSA P-256, built into .NET) with admin-managed allowlists.
- **Encryption:** ECDH key exchange plus AES-GCM session encryption, so chat, admin commands and
  passwords never cross the internet in plaintext. Needed before public dedicated servers (Phase 4).

### 7.3 Our control protocol

- **Encoding:** hand-written, span-based encoding with **explicit message IDs** and **hard size
  limits**.
  - No reflection.
  - **Never** a type name or type ID from the wire mapped to arbitrary .NET types. This is the exact bug
    SE2 Online's first review found.
- **Handshake:** `Hello { protocol version, plugin version, game build, mod-list hash, capabilities }`.
  Any mismatch is a clean, explained refusal.
  - The protocol version is bumped on **any** wire change, following the Luna Multiplayer rule.
- **Messages:** auth, join and world transfer, player list, chat, kick and ban, admin commands, and
  feature-module side channels.
- **Keen replication data** is carried on dedicated channels as opaque, length-prefixed blobs. The
  server still validates everything clients send (§9).

### 7.4 Join flow (target)

1. Connect, then run the handshake and auth.
2. Check the mod list. Any missing mods are listed for the player to subscribe to in the Workshop. We
   never redistribute mod files.
3. World transfer:
   - The preferred path is Keen's own client pull, if it works out of process
     (`GameClientNetworkingComponent.PullDataFromServerAsync` [P0]).
   - Otherwise, a chunked, resumable, hash-verified transfer of the server's snapshot.
   - Voxel edits that Keen doesn't replicate go through a feature module.
4. Spawn. Keen's replication then takes over.

### 7.5 Lessons from prior art that we adopt

- **Server authority:** the host or headless server simulates. Clients send input and requests, and the
  server validates them, following the SE1 and Keen design.
- **Interest management:** use Keen's `SceneSubset` replication (spatial relevance) rather than sending
  the whole world to every client [P0].
- **Ownership:** where we must arbitrate ourselves, use server-granted ownership locks with **epochs**,
  and revoke and reassign them on disconnect.
- **Echo suppression:** while a remote change is being applied, our patches don't re-broadcast it.
- **No world-rewinding features online:** pause, quickload and time manipulation are disabled while
  connected.

## 8. Server modes

| Mode | What it is | Phase |
|---|---|---|
| Listen server | The host plays and the host's game is the server. Steam or direct. | 1–2 |
| Headless dedicated (Windows) | The game runs headless with rendering off: `-tether:server <config>`. | 4 |
| Headless dedicated (Linux) | The same, under Wine/Proton, or natively via Pulsar + LinuxCompat, plus a Docker recipe. | 4 |

- **Game files on the server:** the operator installs the game with **their own Steam account**
  (Steam or SteamCMD with a login). We never ship Keen's files. If Keen later publishes a
  dedicated-server app, we switch to it.
- **Server config:** a JSON file covering world, port, max players, password, admins, MOTD, backup
  schedule and mod list.
- **Admin access:** a remote console over our authenticated protocol. It is not a separate plaintext
  RCON port.
- **Persistence:** the server saves through the game's own save system, on a schedule and with rotating
  backups.

## 9. Security model

**Threat model:** a malicious client, a malicious server, and anyone on the network path.

- **Hostile input:**
  - All inbound bytes are hostile.
  - Every parser is bounds-checked and size-limited, with fuzz tests.
  - Connections are rate-limited and have per-connection memory caps.
  - A malformed packet disconnects that peer and never takes down the host.
- **Clients never write arbitrary state.** Client-to-server traffic is limited to input and to requests
  the server validates (build, grind, terminal change, inventory move). Each request is checked for
  permission, distance and ownership.
- **Protection from a malicious server:**
  - The server can't make the client load code, resolve types, or write outside the game's data folders.
  - World transfer files are size-capped and path-sanitised.
- **No system-level hooks:** no OS-wide keyboard hooks and no native code of our own.
- **Privacy:**
  - No telemetry and no phoning home.
  - Logs stay local, and crash reports are something the user chooses to share.
  - A future server list is opt-in, self-hostable, and documents exactly what it publishes.
- **Security issues** get a `SECURITY.md` with private reporting via GitHub Security Advisories.

## 10. Linux

The **same managed DLL** runs everywhere. "Building for Linux" therefore means:

1. **Developing on Linux:** the .NET SDK builds against the game DLLs in your Steam library
   (`~/.steam/steam/steamapps/common/SpaceEngineers2/Game2`). The path is configurable.
2. **Proton players:**
   - `install.sh` locates the library and the Proton prefix.
   - The launch option uses a Windows-style path, for example `-plugins:Z:\home\<you>\...\Tether.dll`
     [P0: confirm the path form].
3. **Native Linux players:** Pulsar + LinuxCompat. Our code avoids Windows P/Invoke and Windows-only
   APIs; this is enforced by an analyzer or CI check.
4. **Linux dedicated servers** (§8).
5. **CI:** every build and test runs on both `ubuntu-latest` and `windows-latest`.

## 11. Build, CI, releases and trust

- **Toolchain:**
  - SDK pinned via `global.json`, with NuGet lock files (`--locked-mode`).
  - `Deterministic`, `ContinuousIntegrationBuild` (in CI), SourceLink, and embedded PDBs.
- **Game references:**
  - An MSBuild property `Game2Dir`, auto-detected (Windows registry key for App 1133870; Linux Steam
    paths) and overridable in an untracked `Directory.Build.props.user`.
  - References use `Private=false`, so game DLLs are never copied to output.
- **CI without the game:**
  - Public CI builds and tests `Tether.Core` fully.
  - `Tether.Plugin` needs game DLLs, which CI can't legally have. Options **[DECIDE later]**:
    - a self-hosted runner;
    - reference stubs, but only with Keen's written permission (EULA §3);
    - relying on PluginHub plus local builds.
- **Releases:**
  1. **PluginHub:** compiled from source on the player's machine. Nothing to trust but the code.
  2. **GitHub Releases:** a zip containing our DLL, `0Harmony.dll`, LiteNetLib, `SHA256SUMS` and the
     exact game build it was built against. Because builds are deterministic, anyone with the same game
     build can rebuild and compare hashes. A `verify` script automates that. Add build provenance
     attestations once CI can build the plugin.
- **Repo hygiene:**
  - No prebuilt binaries in git.
  - **Never commit decompiled game code.** Engine notes describe APIs in our own words, and only names
    and signatures needed for interop appear in our code.
  - **No code copied from SE2 Online** (non-OSI license) or the SE1 source (EULA-restricted). Read for
    understanding only, and say so in `CONTRIBUTING.md`.

## 12. Surviving game updates

Keen is actively building multiplayer, so the internals we touch **will** move. Defences:

- Keep a list of supported game builds (`SupportedBuilds.json`). On an unknown build we warn, run the
  patch self-check, and disable networking cleanly if critical targets are missing. We never crash the
  game.
- **Patch self-check:** at startup and as a dev tool, every Harmony target is resolved and its signature
  verified, then we report what's broken. The same check runs as a smoke test on dev machines after
  each game update.
- Prefer patching stable entry points (component lifecycles) over deep internals. Use prefix and postfix
  patches; use transpilers only as a last resort and with IL-shape tests.
- Feature isolation (feature modules in §5) means one renamed method costs one feature, not the mod.
- Handle ReadyToRun inlining: patch callers rather than tiny methods, verify each patch fires in a debug
  counter, and document `DOTNET_ReadyToRun=0` as a diagnostic.
- The plugin `IPlugin` API itself has moved namespaces before, so the bootstrap is kept tiny and isolated.

## 13. Proposed repository layout

```
Tether/
├─ docs/                     PLAN.md, research/, adr/ (decision records), engine-map.md (Phase 0)
├─ src/
│  ├─ Tether.Core/            game-independent: Protocol/, Transport/ (abstraction, LiteNetLib, loopback),
│  │                         Session/, Config/, Serialization/
│  └─ Tether.Plugin/          IPlugin bootstrap, Engine/ (adapter), Features/, Steam/, UI/, GlobalUsings.cs
├─ tests/
│  ├─ Tether.Core.Tests/      unit, protocol round-trip, fuzz, loopback integration, simulated lag/loss
│  └─ Tether.Plugin.Checks/   patch-target self-check (run on machines with the game)
├─ tools/                    install.ps1, install.sh, verify scripts, Game2 path detection props
├─ pluginhub/                PluginHub XML template (SourceDirectories: src/Tether.Core, src/Tether.Plugin)
├─ .github/workflows/        ci.yml (ubuntu + windows), release.yml
├─ Directory.Build.props, global.json, LICENSE, README.md, CONTRIBUTING.md, SECURITY.md
```

## 14. Roadmap

Each phase has an exit criterion. Phase "now" needs no game, so it can start before you're back at the
workstation.

| Phase | Scope | Done when |
|---|---|---|
| **Now** (no game needed): **done 2026-11-26** | Repo skeleton, CI on Linux and Windows, `Tether.Core` (transport abstraction, LiteNetLib and loopback backends, protocol and handshake, session state machine), tests and fuzzing, install scripts, CONTRIBUTING and SECURITY docs, and a diagnostics-only plugin bootstrap. Also the first **test kit**: plugin, in-game network self-test, `tether-nettest`, and a reproducible CI package with end-to-end tests. | Two in-process "fake game" peers handshake, exchange channelled data under simulated 200 ms lag and 5% loss, and reject malformed input. 89 tests plus end-to-end runs on Linux and Windows cover this. |
| **0: Discovery** (workstation) | The checklist below. Output: `docs/engine-map.md` in our own words. | We know Keen's transport seam, world-join path, and what replicates out of the box. Architecture A is confirmed or revised. |
| **1: First contact** | Hello-world plugin, then the engine adapter over Direct/LiteNetLib between two game instances on one PC. | Two players on a LAN see each other walk, jetpack and place a block. |
| **2: Steam** | Steam backend (relay P2P), lobbies, invites, join via friends, auth tickets. | A friend behind NAT joins via a Steam invite. |
| **3: Gameplay parity** | Building, grinding, welding, piloting, connectors and subgrids, inventory, mining and voxels for late joiners, power and conveyors, chat, ownership and factions, respawn. Feature modules plus a test matrix. | A 4-player, 2-hour session plays without desync-breaking bugs. |
| **4: v1 release** | Multiplayer UI polish, PluginHub submission, docs, reproducible release pipeline. | v1.0 on PluginHub and GitHub Releases. |
| *Post-v1: Dedicated servers* | Headless mode (Windows, then Linux), config, remote admin, backups, encryption on the direct backend, a Docker recipe, an opt-in self-hostable server list. | A public 8+ player Linux server runs for 24 hours. |
| **Ongoing** | Game-update response (§12), and tracking Keen's official co-op so we converge. | Each game patch is triaged within days. |

### Phase 0 checklist (for the workstation)

Step-by-step instructions are in [`PHASE0.md`](PHASE0.md). The diagnostics-only plugin bootstrap
answers items 1 and 2 and most of 4 automatically (`-tether:debug -tether:dump-api`).

1. Record the game build and the contents of `Game2/SpaceEngineers2.runtimeconfig.json`: the framework
   version, and whether `System.StartupHookProvider.IsSupported` is set.
2. Build a hello-world `IPlugin` that logs to a file. Load it via `-plugins:` on Windows and on Proton,
   and record the path syntax that works on Proton.
3. Test `-allowMultiple`: does it let two instances run on one PC for local testing?
4. **[DECIDE: EULA, §16]** Inspect `Keen.VRage.Multiplayer.*` and `Keen.Game2.Game.Networking` with
   ILSpy, and write the API map in our own words. Questions to answer:
   - Is there a transport interface we can implement?
   - How does `InProcess` replication differ from `OutOfProcess`?
   - What do `SceneSubset` and `NetworkStories` do?
   - How does a client get the initial world?
   - How do `GameSessionHostComponent` and the dedicated-server component work?
5. Run a session with `-enableMultiplayer` **offline or LAN only** and note what works: the Players tab,
   hosting, and what Steam calls it makes. This is to learn the design, not to use Keen's services.
6. Count replication bytes per second in single-player (a temporary patch on the in-process path). This
   gives our first bandwidth estimate.
7. Check whether candidate patch targets are affected by ReadyToRun inlining.
8. Watch the 2026-11-30 Keen announcement and the 2026-12-04 ModAPI news, then revisit §4 if needed.

**Practical note:** testing the Steam backend for real needs **two Steam accounts on two machines** (a
friend, a second PC, or a Steam Deck). The Direct backend can be tested on one PC.

## 15. Testing strategy

- **Core unit tests:** encoders and decoders round-trip, handshake rules, session state machine, chunked
  transfer and resume.
- **Fuzzing:** random and mutated packets against every decoder. The rule is no exceptions escaping, no
  unbounded allocation, and the peer gets disconnected.
- **Loopback and LiteNetLib integration** with LiteNetLib's built-in latency and loss simulation.
- **In-game checks (dev machines):**
  - the patch self-check;
  - a scripted two-instance smoke test once Phase 1 exists;
  - a manual test matrix per feature (host and client × Windows, Proton and native Linux).
- **Soak:** long sessions on a headless server with bot clients, later on.

## 16. Legal and community

Not legal advice, just a reading of Keen's EULA (2025-06-24) and how we stay on the right side of it.

- **§2 Modding:** allowed when it's free, for game owners only, clearly marked as modified, and credits
  Keen. We do all of that: free, no Keen files shipped, "unofficial" in the name and README, and a
  trademark notice.
- **§3 Interference:** what is banned is *malicious* interference ("tunneling, code injection, denial of
  service attacks, or creating private servers"). We use the game's own `-plugins:` hook and never touch
  Keen's backend services. "Private servers" is the clause most relevant to dedicated servers, and the
  strongest reason to ask Keen directly.
- **§3 Reverse engineering:** decompiling needs Keen's written permission. Every SE2 plugin author
  currently decompiles in practice, and EU law (Keen is Czech; Directive 2009/24/EC Art. 6) has a narrow
  interoperability exception. Even so, **we should ask Keen** for written permission covering:
  - decompiling for interoperability;
  - running community servers;
  - ideally, publishing reference stubs for CI.
- **Precedent:** for SE1, Keen moved plugins to a community open-source loader and requires them to be
  open source. That is exactly this project's stance, and PluginHub's review process is the SE2
  continuation of it.

## 17. Open decisions

**Decided (2026-11-26):**

- **Name: Tether.** It replaces "SE2-MP", which was too close to the closed SE2MP (se2mp.com). The
  GitHub repository itself should be renamed to match.
- **License:** GPL-3.0-or-later with a game-linking exception ([`LICENSE-EXCEPTION.md`](../LICENSE-EXCEPTION.md)).
- **v1 scope:** a small co-op listen server over Steam P2P (§1).

**Still open:**

1. **[DECIDE] Contact Keen?** Recommended before Phase 0 decompiling (§16). This matters less for v1
   now that dedicated servers ("private servers") are out of its scope.
2. **[DECIDE later]** How CI builds the plugin DLL (§11).
3. **[Revisit after Phase 0]** Architecture A versus C, and Keen's transport seam.
