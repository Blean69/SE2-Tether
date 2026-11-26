// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Reflection;
using HarmonyLib;
using Keen.VRage.Core.Services.GameSessions;
using Tether.Diagnostics;

namespace Tether.Game.Sessions;

/// <summary>
/// The session for the world you are hosting. Tether keeps its visibility at the player's choice: the game
/// itself would set it to hidden, and later calls from the game can't flip it back.
/// </summary>
internal static class HostedSession
{
    private static readonly object Gate = new();
    private static Type? s_guardedType;
    private static bool s_applying;

    public static IHostedGameSession? Current { get; private set; }

    public static HostVisibility Visibility { get; private set; } = HostVisibility.Hidden;

    private static ITetherLog Log => GameModule.Log;

    public static void Attach(IHostedGameSession session)
    {
        Current = session;
        Visibility = GameModule.Settings.Visibility;
        Log.Info($"Hosting: session ready. {DescribeCode(session)}");
        InstallGuards(session.GetType());
        Apply(Visibility);
    }

    /// <summary>Changes who can join the hosted world. Used by the in-game panel.</summary>
    public static void SetVisibility(HostVisibility visibility)
    {
        Visibility = visibility;
        Apply(visibility);
    }

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
