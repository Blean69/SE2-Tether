// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Tether.Diagnostics;

namespace Tether.Game.Sessions;

/// <summary>
/// Stops the pause menu from freezing a world that other players are in, or can join.
/// </summary>
/// <remarks>
/// <para>
/// In single player, opening the pause menu pauses the world: the game's <c>AutoPauseWorldComponent</c> sets
/// <c>TickManagementComponent.Pause</c> when <c>PauseWhenInMenu</c> is on. When you host, your game runs the
/// world for everyone, so that pause freezes every joined player as well, and someone joining while the host sits
/// in the menu has to wait until the host closes it.
/// </para>
/// <para>
/// While the hosted world is open to others, or anyone is connected, Tether keeps the world running when the menu
/// opens, and unpauses it if it was paused when that became true. Otherwise the game's own behaviour is unchanged.
/// </para>
/// </remarks>
internal static class PauseInMultiplayer
{
    private const string AutoPauseType = "Keen.Game2.AutoPauseWorldComponent";

    private static FieldInfo? s_tickField;
    private static FieldInfo? s_pauseWhenInMenu;
    private static PropertyInfo? s_pause;
    private static WeakReference<object>? s_tickManagement;
    private static bool? s_gameDefault;
    private static bool s_warned;

    public static void Install(Harmony harmony)
    {
        var autoPause = AccessTools.TypeByName(AutoPauseType) ?? throw new TypeLoadException(AutoPauseType);

        // An explicit interface implementation: its name is "<interface>.OpenInGameMenu".
        var open = autoPause.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.Name.EndsWith("OpenInGameMenu", StringComparison.Ordinal))
            ?? throw new MissingMethodException(AutoPauseType, "OpenInGameMenu");
        s_tickField = AccessTools.Field(autoPause, "_tickManagement") ?? throw new MissingFieldException(AutoPauseType, "_tickManagement");
        var tickType = s_tickField.FieldType;
        s_pauseWhenInMenu = AccessTools.Field(tickType, "PauseWhenInMenu") ?? throw new MissingFieldException(tickType.FullName, "PauseWhenInMenu");
        s_pause = AccessTools.Property(tickType, "Pause") ?? throw new MissingMemberException(tickType.FullName, "Pause");

        harmony.Patch(open, prefix: new HarmonyMethod(typeof(PauseInMultiplayer), nameof(OpenMenuPrefix)));
    }

    /// <summary>Decides, each time the menu opens, whether this opening may pause the world.</summary>
    public static void OpenMenuPrefix(object __instance)
    {
        try
        {
            var tick = s_tickField!.GetValue(__instance);
            if (tick is null)
            {
                return;
            }

            s_tickManagement = new WeakReference<object>(tick);
            s_gameDefault ??= (bool)s_pauseWhenInMenu!.GetValue(tick)!;
            var keepRunning = HostedSession.KeepsWorldRunning;
            s_pauseWhenInMenu!.SetValue(tick, s_gameDefault.Value && !keepRunning);
            if (keepRunning)
            {
                GameModule.Log.Debug("Pause menu: the world keeps running, because other players are in it or can join.");
            }
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
        }
    }

    /// <summary>Unpauses a world the menu paused, once other players are in it or can join.</summary>
    public static void Refresh()
    {
        if (!HostedSession.KeepsWorldRunning || s_tickManagement is null || !s_tickManagement.TryGetTarget(out var tick))
        {
            return;
        }

        try
        {
            if ((bool)s_pause!.GetValue(tick)!)
            {
                // The menu's own counter sets Pause again (to false) when it closes, so this doesn't fight it.
                s_pause.SetValue(tick, false);
                GameModule.Log.Info("Pause menu: unpaused the world, because other players are in it or can join.");
            }
        }
        catch (Exception ex)
        {
            WarnOnce(ex);
        }
    }

    private static void WarnOnce(Exception ex)
    {
        if (!s_warned)
        {
            s_warned = true;
            GameModule.Log.Warn("Pause menu: couldn't keep the world running; the pause menu may pause it for everyone.", ex);
        }
    }
}
