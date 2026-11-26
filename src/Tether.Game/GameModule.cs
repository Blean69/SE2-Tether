// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using HarmonyLib;
using Tether.Diagnostics;
using Tether.Game.Sessions;
using Tether.Runtime.Modules;

namespace Tether.Game;

/// <summary>
/// Tether's in-game multiplayer layer. It switches on Space Engineers 2's own (unreleased) multiplayer
/// session system and adds what the game doesn't expose yet: control over who can join, the join code,
/// joining by code, and clear logging of every step.
/// </summary>
public sealed class GameModule : ITetherGameModule
{
    internal const string HarmonyId = "dev.tether.game";

    private Harmony? _harmony;

    internal static ITetherLog Log { get; private set; } = NullLog.Instance;

    internal static TetherSettings Settings { get; private set; } = TetherSettings.Default;

    public void Start(GameModuleContext context)
    {
        Log = context.Log;
        Settings = TetherSettings.FromOptions(context.Options, Log);
        if (!Settings.MultiplayerEnabled)
        {
            Log.Info("Multiplayer: off (-tether:mp=off). The game runs as normal single-player.");
            return;
        }

        KeenMultiplayer.Enable(Log);
        _harmony = new Harmony(HarmonyId);
        var installed = SessionPatches.InstallAll(_harmony, Log);
        Log.Info($"Multiplayer: on. {installed} patch group(s) installed. Hosted worlds start as: {Settings.Visibility}.");
    }

    public void OnBeforeEngineInstantiated()
    {
        if (Settings.MultiplayerEnabled)
        {
            // The game may read its launch arguments again while building the engine: make sure our choice sticks.
            KeenMultiplayer.Enable(Log);
        }
    }

    public void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }
}
