// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Keen.VRage.Core.Services.Data;
using Keen.VRage.Core.Services.GameSessions;
using Keen.VRage.Core.Services.Networking;
using Tether.Diagnostics;
using Tether.Game.UI;

namespace Tether.Game.Sessions;

/// <summary>
/// The session for the world you are hosting. Tether keeps its visibility at the player's choice: the game
/// itself would set it to hidden, and later calls from the game can't flip it back.
/// </summary>
internal static class HostedSession
{
    private static readonly object Gate = new();
    private static readonly HashSet<GameServiceUser> s_connected = new();
    private static Type? s_guardedType;
    private static bool s_applying;

    public static IHostedGameSession? Current { get; private set; }

    public static HostVisibility Visibility { get; private set; } = HostVisibility.Hidden;

    /// <summary>True while other players are in the hosted world or can join it: then it must keep running.</summary>
    public static bool KeepsWorldRunning
    {
        get
        {
            lock (s_connected)
            {
                return Current is not null && (Visibility != HostVisibility.Hidden || s_connected.Count > 0);
            }
        }
    }

    private static ITetherLog Log => GameModule.Log;

    /// <summary>
    /// Forgets the previous world's session as soon as the game starts hosting a new one, so the guard doesn't apply
    /// the old world's visibility to the new session before <see cref="Attach"/> runs.
    /// </summary>
    public static void Reset()
    {
        Current = null;
        Visibility = GameModule.Settings.Visibility;
        lock (s_connected)
        {
            s_connected.Clear();
        }
    }

    public static void Attach(IHostedGameSession session)
    {
        Reset();
        Current = session;
        Log.Info($"Hosting: session ready. {DescribeCode(session)}");
        InstallGuards(session.GetType());
        Apply(Visibility);
        Observe(session);
        Toasts.Show(Visibility == HostVisibility.Hidden
            ? "Multiplayer is ready. Your world is closed: press Ctrl+M to open it to friends."
            : $"Your world is open ({Visibility}). Join code {session.JoinCode?.ToDisplayString() ?? "pending"}. Ctrl+M for options.");
    }

    /// <summary>Changes who can join the hosted world. Used by the in-game panel.</summary>
    public static void SetVisibility(HostVisibility visibility)
    {
        Visibility = visibility;
        Apply(visibility);
        Toasts.Show(TetherSettings.Describe(visibility));
        PauseInMultiplayer.Refresh();
    }

    /// <summary>Watches who knocks, so the host (and the logs) can see joins happen or fail.</summary>
    private static void Observe(IGameSession session)
    {
        try
        {
            var communication = session.Communication;
            if (communication is null)
            {
                return;
            }

            communication.ConnectionRequested += user =>
            {
                Log.Info($"Hosting: {Describe(user)} is connecting.");
                Toasts.Show($"{Describe(user)} is joining your world.");
                lock (s_connected)
                {
                    s_connected.Add(user);
                }

                PauseInMultiplayer.Refresh();
            };
            communication.PeerConnectionFailed += (user, reason) =>
            {
                lock (s_connected)
                {
                    s_connected.Remove(user);
                }

                Log.Info($"Hosting: connection with {Describe(user)} ended: {reason}.");
                if (reason != ConnectionFailureReason.ClosedByLocalUser)
                {
                    Toasts.Show($"{Describe(user)} disconnected ({reason}).");
                }
            };
        }
        catch (Exception ex)
        {
            Log.Debug($"Hosting: can't observe connections: {ex.Message}");
        }
    }

    private static string Describe(GameServiceUser user) => user.ToString() ?? "A player";

    private static void Apply(HostVisibility visibility)
    {
        var session = Current;
        if (session is null)
        {
            return;
        }

        lock (Gate)
        {
            s_applying = true;
            try
            {
                session.SetJoinableType(TetherSettings.ToGame(visibility));
            }
            finally
            {
                s_applying = false;
            }
        }

        Log.Info($"Hosting: {TetherSettings.Describe(visibility)}");
    }

    private static string DescribeCode(IGameSession session) =>
        session.JoinCode is { } code ? $"Join code: {code.ToDisplayString()}" : "No join code yet.";

    /// <summary>Patches the concrete session type (internal to the game) once, the first time we see it.</summary>
    private static void InstallGuards(Type sessionType)
    {
        if (s_guardedType == sessionType)
        {
            return;
        }

        try
        {
            var harmony = new Harmony(GameModule.HarmonyId);
            var setType = AccessTools.Method(sessionType, nameof(IHostedGameSession.SetJoinableType));
            if (setType is not null)
            {
                harmony.Patch(setType, prefix: new HarmonyMethod(typeof(HostedSession), nameof(SetJoinableTypePrefix)));
            }

            var setCode = AccessTools.Method(sessionType, nameof(IHostedGameSession.SetJoinCode));
            if (setCode is not null)
            {
                harmony.Patch(setCode, postfix: new HarmonyMethod(typeof(HostedSession), nameof(SetJoinCodePostfix)));
            }

            s_guardedType = sessionType;
            Log.Debug($"Hosting: guarding visibility and join code on {sessionType.Name}.");
        }
        catch (Exception ex)
        {
            Log.Warn("Hosting: could not guard the session's visibility; the game may reset it to hidden.", ex);
        }
    }

    private static void SetJoinableTypePrefix(ref JoinableSessionType __0)
    {
        if (s_applying || Current is null)
        {
            return;
        }

        var wanted = TetherSettings.ToGame(Visibility);
        if (__0 != wanted)
        {
            Log.Debug($"Hosting: the game asked for visibility {__0}; Tether keeps {wanted}.");
            __0 = wanted;
        }
    }

    private static void SetJoinCodePostfix(object __instance)
    {
        if (__instance is IGameSession session && ReferenceEquals(session, Current))
        {
            Log.Info($"Hosting: {DescribeCode(session)}");
        }
    }
}
