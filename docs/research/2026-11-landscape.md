# Research notes: SE2 multiplayer landscape (2026-11-26)

These notes back up [`docs/PLAN.md`](../PLAN.md). Each claim carries a confidence tag:

- **[C] Confirmed**: an official source, several sources that agree, or source code we read ourselves.
- **[R] Reported**: one community source, often based on decompiled code.
- **[?] Unverified**: needs checking on a machine with the game installed. Most of these feed the
  Phase 0 checklist in the plan.

Nothing here comes from decompiling the game ourselves. The internals listed below come from
public sources, Keen's own patch notes, and public open-source plugins.

---

## 1. The game

| Topic | Finding | Conf. |
|---|---|---|
| Steam App ID | 1133870 | C |
| Official multiplayer | None yet. The store lists the game as single-player only. | C |
| Roadmap | "4-player co-op (player-hosted sessions)" is planned for **early 2027**. Dedicated servers are promised "later". A "Special Announcement" livestream is on **2026-11-30**. A C# ModAPI hackathon runs **2026-12-04**; the API is internal-only for now. | C |
| Engine design | Marek Rosa: VRAGE3 was designed for multiplayer, with game logic as "server" code and render/animation as "client" code. MP was "50% already in the game". | C |
| Replication in SP | Single-player runs an in-process server with the local player as a client. Hotfix notes name `InProcessReplicationSessionComponent`, `NetworkStoryContext` and replication crashes. | C |
| Hidden MP | Game 4.0 contains Keen's own unfinished multiplayer behind the `-enableMultiplayer` launch flag, toggled via `Keen.Game2.Simulation.GameFeatureConfig.MultiplayerEnabled`. | C (seen in SE2 Online source) |
| Multiplayer namespaces | `Keen.VRage.Multiplayer.{Replications, Replications.InProcess, Replications.SceneSubset, NetworkStories, Transport, Transport.OutOfProcess.{Client,Server}, Sessions, SecureIds, Replay}`; `Keen.Game2.Game.Networking`; `GameSessionHostComponent` (Steam lobby); `GameSessionDedicatedServerComponent`; a headless engine. | R→C |
| Runtime | Stock game: **.NET 9**, framework-dependent. `SpaceEngineers2.exe` is an apphost that loads `SpaceEngineers2.dll` with the shared runtime. Pulsar's launcher runs it on **.NET 10**. Plugins that target `net9.0` work in both. | C |
| Entry point | `Keen.Game2.Program.Main` in `Game2/SpaceEngineers2.dll` | C |
| Assemblies | `Game2.{Game,Client,Simulation,...}.dll` and `VRage.{Library,Core,DCS,Multiplayer,Physics,Steam,Voxels,UI,...}.dll`, plus Avalonia UI, Vortice/D3D12, Roslyn, Steamworks.NET and FMOD. Most are ReadyToRun-compiled but still contain IL. | R→C |
| Native parts | Havok physics (`VRage.Physics.Native.dll`), voxels, and Kythera AI. Rendering is DX12. | C/R |
| Obfuscation | None. Stack traces show readable names and line numbers. | C |
| ECS | "DCS", a data-oriented ECS. Block definitions come as `*_ServerComposition.def` / `*_ClientComposition.def` pairs. | C |
| Saves | Binary `.vrb` files (`savegame.vrb`, `sessioncomponents.vrb`, `snapshots.vrb`) plus JSON `.container-info`, under `%APPDATA%\SpaceEngineers2\AppData\SaveGames\`. | R |
| Determinism | A `DeterministicRuntime` flag exists and is off by default. Replication syncs state; it is not lockstep. | C/R |
| Linux | Not officially supported. ProtonDB rates it **Gold** (180 reports); Steam Deck "Playable". Keen fixed a Proton rendering bug in Oct 2026. | C |
| Official modding | Data-only Mod SDK (Jun 2025): "Scripting and code APIs are not included yet." | C |

## 1a. Confirmed on hardware (Phase 0, 2026-11-26)

These were observed on a real machine: Ubuntu 26.04 laptop, Steam with GE-Proton11-1, Steam build
26536832, game 4.0.0.3169. Tether's diagnostics plugin was loaded via `-plugins:`. Everything below
comes from the game's own files and logs plus Tether's log. No game code was decompiled.

- **Runtime:**
  - `SpaceEngineers2.runtimeconfig.json` targets `net9.0` and needs Microsoft.NETCore.App and
    Microsoft.WindowsDesktop.App 9.0.0.
  - Proton installed .NET 9.0.22 into the prefix.
  - The startup-hook switch isn't set, so that fallback loading route stays available.
- **Plugin loading:**
  - Loading via `-plugins:` works under Proton.
  - The game logs "Plugin NOT loaded: <path>" and shows an "IncorrectPluginPath" message for a bad path.
  - On Linux the launch option must be single-quoted, because Steam's shell drops unquoted backslashes.
  - Tether's dependencies resolved from the plugin folder (`C:\Tether`).
- **PluginHost (names via reflection):**
  - Events: `OnBeforeProjectsLoaded(List<VRageProject>)`, `OnBeforeEngineInstantiated(EngineBuilder)`.
  - Methods: `Add`, `Dispose`, `InvokeOnBeforeEngineInstantiated`, `InvokeOnBeforeProjectsLoaded`,
    `PostEngineInit`, and `TryAddFromAssembly` (two overloads).
  - Both events fired, in that order, about 0.2 s apart and before the engine was built.
- **Steam:** the game ships **Steamworks.NET 2024.8.0.0**, already loaded when plugins start.
- **Networking inside the game process:** a LiteNetLib UDP host started by the plugin worked under
  Proton. A native-Linux `tether-nettest` on the same machine joined it, chatted both ways, and lost
  0% of link probes.
- **Keen's own API documentation:** the game ships XML doc files next to its assemblies
  (`VRage.Core.xml`, `VRage.Multiplayer.xml` and others). These are Keen's published documentation of
  the public API, and the first place to look when mapping the netcode for Phase 1.
- **Assemblies present:** `VRage.Core`, `VRage.Library`, `VRage.Multiplayer`, `VRage.Steam`,
  `Steamworks.NET`, and `VRage.Library.Generator` (the assembly that causes duplicate types in Pulsar
  builds).
- **Proton paths:**
  - Game folder: `S:\common\SpaceEngineers2\Game2\`, a Wine drive mapped to the Steam library.
  - Data folder: `C:\users\steamuser\AppData\Roaming\SpaceEngineers2`.

## 2. Plugin loading

- **Built-in hook [C]:** launching with `-plugins:<dll>[;<dll>...]` loads any public class that implements
  `Keen.VRage.Core.Plugins.IPlugin`.
  - The class's constructor receives a `PluginHost` exposing `Args`, `OnBeforeProjectsLoaded` and
    `OnBeforeEngineInstantiated`.
  - The namespace moved between 2025 and 2026 builds, so this API is not stable.
  - No injector, proxy DLL or startup hook is needed.
- **Pulsar [C]** (github.com/SpaceGT/Pulsar, MIT) is the community successor to the SE1 Plugin Loader.
  - Its `Modern` launcher hosts SE2 on .NET 10, patches `PluginHost`, and calls `Program.Main`.
  - **PluginHub plugins are compiled on the player's machine from a pinned commit of their public
    GitHub source** (StarCpt/PluginHub-SE2, about 20 plugins, PRs reviewed by maintainers).
  - Its Cecil preloader can strip ReadyToRun code from patched assemblies.
- **Pulsar constraints [C]:**
  - Only the listed `SourceDirectories` are compiled, by Pulsar's own Roslyn rather than MSBuild. That
    means no `ImplicitUsings`, and source generators should be treated as unavailable.
  - NuGet dependencies must be declared in the hub XML.
  - `VRage.Library.Generator` pulled in transitively causes duplicate-type errors (CS0433); SE2 Online
    works around it with a stub.
  - Under Pulsar, a plugin must register with the game's type index (`MetadataManager.PushContext`) or
    worlds won't load.
- **LinuxCompat [C]** (CometWorks/linux-compat2, AGPL-3.0) runs SE2 natively on Linux through Pulsar,
  using DXVK, vkd3d-proton and SDL3, without recompiling any game assemblies.
- **Older loaders:** PluginLoader2 (inactive since 2025-06), SE2SimplePluginLoader, SE2PL. They are
  superseded and we won't target them.
- **Patching library:** Lib.Harmony 2.4.x (2.4.3 is current; 3.0 is in RC), which Pulsar and every
  SE2 plugin we found use.
  - Known gotchas: ReadyToRun inlining can bypass patches; Harmony 2.4.2 can't patch methods with
    exception filters; open generic methods can't be patched.
  - MonoMod detects Wine and uses its Windows backend.

## 3. Existing multiplayer mods

### SE2 Online (yellow51 / Vyrex Interactive LLC)

- **Source and license [C]:** github.com/yellow5111/SE2Online, v0.18.5, about 545 C# files. Its
  license is a custom non-commercial one and is **not OSI-approved**.
  - The website gates downloads behind "Sign in through Steam". This is the one users find suspicious.
  - The source itself is public on GitHub and is on PluginHub, but the website doesn't link to it.
- **How it hooks in [C]:**
  - Loads via `-plugins:` or Pulsar.
  - **Keeps Keen's replication layer** and swaps out the transport: Harmony prefixes skip
    `OutOfProcess*PeerTransportSessionComponent` methods and route the bytes through its own link.
  - Forces Keen's `-enableMultiplayer` path off.
- **Transport [C]:** Steamworks.NET `SteamNetworkingSockets` (P2P plus IP sockets) with its own framing,
  fragmentation, acks and compression.
- **Server model [C]:** server-authoritative; clients send input only. It installs about 80 feature
  patches, each guarded so a game update disables one feature instead of crashing.
- **Dedicated server [C]:** the game run headless.
- **Lessons from its PluginHub review [C]:**
  - The first submission let wire data choose .NET types (the BinaryFormatter class of bug), let clients
    overwrite arbitrary component data, and let malformed packets kill host networking. These were
    fixed before approval.
  - Early history contained a prebuilt DLL and decompiled game code, both later removed.
  - Its standalone server package redistributes about 6.9 GiB of Keen runtime files.
  - It installs a Windows low-level keyboard hook, and an opt-in server list sends SteamIDs to the
    author's server.
  - With no streaming radius, every client gets the whole world state.

### SE2MP (se2mp.com, "LucaSkywalker")

- **Distribution [C/R]:** closed source, distributed only via Discord. Signed auto-updates, automatic
  log and crash-dump upload. Hosting is reportedly paid; the report comes from the rival developer.
- **Features [C]:** host, join by code or Steam invite, headless "DsProto" dedicated server, and
  client-side lag hiding.
- **Known limitation [C]:** mined voxels are not synced to joiners ("engine limitation").
- **Note:** its name, "SE2MP", was almost identical to this project's original working name, which is why we renamed to Tether.

## 4. Keen's terms (EULA, updated 2025-06-24, 2.spaceengineersgame.com/eula)

These are verbatim fragments, paraphrased only where noted.

- **§2 Modding:**
  - Mods are allowed "solely for use within the Game, provided that such Mods are distributed
    exclusively to users who have legally obtained access to the Game."
  - They "must be clearly marked as modified works, must credit us appropriately".
  - "Commercial use of Mods is prohibited without our explicit written permission."
- **§3 Technical Misuse:** "reverse engineering, decompiling, or disassembling the Game without our explicit written permission is prohibited, except as allowed by this Agreement." Cheats and "any software that affects the Game" are forbidden. The scope of that phrase is unclear.
- **§3 Interference:** "maliciously interfering with our or third-party IT systems ... through harmful activities (tunneling, code injection, denial of service attacks, or creating private servers), is not permitted."
- **§3 Usage:** no distributing the Game "in whole or in part" without permission. This rules out shipping Keen DLLs or stub assemblies.
- **§9:** user-generated content may only be made with the official Mod SDK. Whether plugins count as UGC is unclear.
- **SE1 precedent:** Keen's SE1 plugins page says plugins move to a community open-source loader on GitHub, that "All plugins on the list must also be open source", and that they are used at your own risk. Nothing equivalent exists for SE2 yet.

## 5. Prior art outside SE2

| Project | Model | Lessons for us |
|---|---|---|
| **SE1 (Keen)** | Server-authoritative replication with per-client priority, AABB spatial relevance, and state groups sent "latest state, resend on loss". RPCs carry server-side validators. Subgrids sync relative to the master grid. Client prediction came in the 2018 overhaul. | VRAGE3 likely carries these ideas forward. Its EULA forbids reusing SE1 code. |
| **Nitrox** (Subnautica, GPL-3) | Standalone .NET server that doesn't run the game. Per-entity simulation locks (exclusive/transient) granted by visibility. Snapshot interpolation. LiteNetLib transport. JSON saves with migrations. | Echo suppression while applying remote changes. CI test that patch IL still matches the game. Had a near-total rewrite after one big game update. |
| **Luna Multiplayer** (KSP, MIT) | Standalone server. Lock types: Control/Update/UnloadedUpdate/Spectator, plus singleton "world system" locks. Data-driven per-part field sync. | Make remote-owned objects locally immortal. Bump protocol version on every message change. Disable pause/revert/quickload online. |
| **Torch** (SE1 DS, Apache-2) | Wraps the real dedicated server, so the game itself is the authority. | Lags 1–2 days behind game patches. Its WPF UI is Windows-only, which a cross-platform server should avoid. |
| **Skyrim Together** | Partially authoritative C++ server on GameNetworkingSockets. | Ownership **epochs** reject stale requests. |
| **Valheim** | First player in range owns an object, with no rebalancing. | One laggy owner hurts everyone. |
| **Quantum Space Buddies** | Mirror + Steamworks. | Re-initialising Steam as App ID 480 in-process breaks the game's Steam features. Don't. |

## 6. Transport and serialization options

- **Steam (SteamNetworkingSockets via the game's own Steamworks.NET):**
  - Valve's relay (SDR) traverses NAT and CGNAT.
  - Traffic is encrypted and peers are authenticated by SteamID.
  - Lobbies and invites are built in.
  - Steam is already initialised in-process under App ID 1133870, so we use the game's copy and never
    ship our own Steamworks.
- **LiteNetLib (MIT, very active):**
  - Pure C# UDP with reliable/unreliable/sequenced channels, fragmentation and MTU detection.
  - Hole punching needs an introducer server, and there is no relay.
  - Best fit for dedicated servers, LAN and direct IP.
- **Not chosen:**
  - ENet-CSharp: low activity, no advantage over LiteNetLib.
  - Riptide: fine, but a small ecosystem.
  - System.Net.Quic: streams only, needs TLS certificates and msquic.
- **Serialization:**
  - MemoryPack and MessagePack rely on source generators. That is a problem under Pulsar's compiler, and
    MessagePack's typeless mode is a security footgun.
  - Hand-written span-based encoding is zero-allocation, safe and fully under our control.

## Sources

- Keen: https://2.spaceengineersgame.com/roadmap-2/ · https://2.spaceengineersgame.com/mareks-dev-diary-november-26-2026/ · https://2.spaceengineersgame.com/mareks-dev-diary-june-11-2026/ · https://blog.marekrosa.org/2025/01/space-engineers-2-alpha-early-access/ · https://blog.marekrosa.org/2023/04/guest-post-jan-hlousek-vrage3/ · https://2.spaceengineersgame.com/space-engineers-2-hotfix-4-0-0-3169/ · https://2.spaceengineersgame.com/eula/ · https://spaceengineersgame.com/plugins · https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=1133870 · https://store.steampowered.com/app/1133870/
- Support threads (runtime): https://support.keenswh.com/spaceengineers2/pc/topic/51041-game-fails-to-launch · https://support.keenswh.com/spaceengineers2/pc/topic/45873-net-runtime-error
- Linux: https://www.protondb.com/app/1133870 · https://github.com/CometWorks/linux-compat2 · https://github.com/HansKristian-Work/vkd3d-proton
- Loaders and templates: https://github.com/SpaceGT/Pulsar · https://github.com/StarCpt/PluginHub-SE2 · https://github.com/CometWorks/client2-plugin-template · https://github.com/sepluginloader/PluginLoader2 · https://github.com/CaptainChunck/SE2VR
- MP mods: https://github.com/yellow5111/SE2Online · https://se2-online.yellow51.dev/ · https://se2mp.com/ · https://github.com/StarCpt/PluginHub-SE2/pull/16
- Prior art: https://github.com/SubnauticaNitrox/Nitrox · https://github.com/LunaMultiplayer/LunaMultiplayer · https://github.com/KeenSoftwareHouse/SpaceEngineers · https://blog.marekrosa.org/2018/07/space-engineers-multiplayer-overhaul/ · https://github.com/TorchAPI/Torch · https://github.com/tiltedphoques/TiltedEvolution · https://github.com/misternebula/quantum-space-buddies
- .NET mechanics: https://github.com/dotnet/runtime/blob/main/docs/design/features/host-startup-hook.md · https://github.com/pardeike/Harmony · https://github.com/MonoMod/MonoMod · https://github.com/krafs/Publicizer · https://github.com/dotnet/reproducible-builds · https://github.com/actions/attest
- Networking: https://github.com/RevenantX/LiteNetLib · https://github.com/rlabrecque/Steamworks.NET · https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay
