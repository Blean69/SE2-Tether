// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Reflection;
using HarmonyLib;
using Keen.Game2.Game.RuntimeSystems.GameSessions;
using Keen.VRage.Core.Services.GameSessions;
using Tether.Diagnostics;
using KTask = Keen.VRage.Library.Threading.Task;

namespace Tether.Game.Sessions;

/// <summary>
/// Harmony patches on the game's session components. Each group installs independently: if a game update
/// renames something, only that group is lost and the log says which.
/// </summary>
internal static class SessionPatches
{
    // Note: the game defines its own AsyncVoidMethodBuilder, so Tether never uses async void. Fire-and-forget
    // work is an async Task that catches its own exceptions and is discarded with "_ =".
    private static ITetherLog Log => GameModule.Log;

    public static int InstallAll(Harmony harmony, ITetherLog log)
    {
        var installed = 0;
        installed += Try(log, "host session", () => Postfix(harmony, typeof(GameSessionHostComponent), "Init", nameof(HostInitPostfix)));
        installed += Try(log, "client session", () => Postfix(harmony, typeof(GameSessionClientComponent), "Init", nameof(ClientInitPostfix)));
        installed += Try(log, "join requests", () =>
        {
            Prefix(harmony, typeof(GameSessionsCoreComponent), "OnJoinSessionRequested", nameof(JoinRequestedPrefix));
            Postfix(harmony, typeof(GameSessionsCoreComponent), "Init", nameof(CoreInitPostfix));
        });
        return installed;
    }

    // ----- Hosting -------------------------------------------------------------------------------------

    private static void HostInitPostfix(GameSessionHostComponent __instance)
    {
        Log.Debug("Hosting: the game's host component initialised; waiting for its session.");
        _ = WatchHostedAsync(((IHostedGameSessionProvider)__instance).HostedGameSession);
    }

    private static async System.Threading.Tasks.Task WatchHostedAsync(Keen.VRage.Library.Threading.Task<IHostedGameSession> task)
    {
        try
        {
            var session = await task;
            if (session is null)
            {
                Log.Info("Hosting: the game did not create a session for this world.");
                return;
            }

            HostedSession.Attach(session);
        }
        catch (Exception ex)
        {
            Log.Warn("Hosting: the session could not be created or watched.", ex);
        }
    }

    // ----- Joining -------------------------------------------------------------------------------------

    private static void ClientInitPostfix(GameSessionClientComponent __instance)
    {
        Log.Info("Joining: the game started a client session.");
        _ = WatchClientAsync(__instance.GameSession);
    }

    private static async System.Threading.Tasks.Task WatchClientAsync(Keen.VRage.Library.Threading.Task<IGameSession> task)
    {
        try
        {
            var session = await task;
            Log.Info(session is null
                ? "Joining: no session was joined."
                : $"Joining: connected to {session.Server} (Steam lobby: {session.IsLobby}, dedicated: {session.IsDedicated}).");
        }
        catch (Exception ex)
        {
            Log.Warn("Joining: the session failed.", ex);
        }
    }

    private static void JoinRequestedPrefix(GameSessionID sessionId) =>
        Log.Info($"Joining: join requested for {sessionId} (Steam Join Game, an invite, or a join code).");

    private static void CoreInitPostfix(GameSessionsCoreComponent __instance)
    {
        if (GameModule.Settings.JoinCode is { } code)
        {
            _ = JoinByCode.StartAsync(__instance, code);
        }
    }

    // ----- Helpers -------------------------------------------------------------------------------------

    private static int Try(ITetherLog log, string name, Action install)
    {
        try
        {
            install();
            log.Debug($"Patches: '{name}' installed.");
            return 1;
        }
        catch (Exception ex)
        {
            log.Warn($"Patches: '{name}' could not be installed in this game build; that feature is off.", ex);
            return 0;
        }
    }

    private static void Postfix(Harmony harmony, Type type, string method, string patch) =>
        harmony.Patch(Target(type, method), postfix: new HarmonyMethod(typeof(SessionPatches), patch));

    private static void Prefix(Harmony harmony, Type type, string method, string patch) =>
        harmony.Patch(Target(type, method), prefix: new HarmonyMethod(typeof(SessionPatches), patch));

    private static MethodBase Target(Type type, string method) =>
        AccessTools.DeclaredMethod(type, method) ?? throw new MissingMethodException(type.FullName, method);
}

/// <summary>Joins a session by its join code, as given by <c>-tether:join=CODE</c> (until the menu screen exists).</summary>
internal static class JoinByCode
{
    private static bool s_started;

    public static async System.Threading.Tasks.Task StartAsync(GameSessionsCoreComponent core, string text)
    {
        if (s_started)
        {
            return;
        }

        s_started = true;
        var log = GameModule.Log;
        try
        {
            if (!SessionJoinCode.TryParse(text, out var code))
            {
                log.Warn($"Join code '{text}' is not valid.");
                return;
            }

            var service = AccessTools.Field(typeof(GameSessionsCoreComponent), "_gameSessions")?.GetValue(core) as IGameSessionsService;
            if (service is null)
            {
                log.Warn("Joining: the game's session service is not available.");
                return;
            }

            // Let the main menu finish loading first.
            await KTask.Delay(TimeSpan.FromSeconds(3));
            log.Info($"Joining: looking for a session with code {code.ToDisplayString()}...");
            var id = await service.TryFindSessionByCode(code);
            if (id is null)
            {
                log.Info($"Joining: no session found for code {code.ToDisplayString()}. Is the host's world open (visibility public)?");
                return;
            }

            log.Info($"Joining: found {id}; asking the game to join.");
            AccessTools.Method(typeof(GameSessionsCoreComponent), "OnJoinSessionRequested").Invoke(core, new object[] { id });
        }
        catch (Exception ex)
        {
            log.Warn("Joining by code failed.", ex);
        }
    }
}
