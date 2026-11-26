// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using Tether.Diagnostics;
using Keen.Game2.Client.UI.InGame;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.Core.Input;
using Keen.VRage.Input;
using KeenSession = Keen.VRage.Core.Game.Systems.Session;

namespace Tether.Game.UI;

/// <summary>
/// Runs every frame while a world is loaded (a postfix on the game's Session.Update): shows queued toasts
/// and opens the Multiplayer panel on Ctrl+M.
/// </summary>
internal static class InWorldHotkey
{
    private static bool s_wasPressed;
    private static bool s_failedOnce;

    public static void SessionUpdatePostfix(KeenSession __instance)
    {
        try
        {
            // Only the client session has the in-game UI; the server session ticks too and is skipped.
            var inGameUI = __instance.TryGet<SessionInGameUISessionComponent>();
            if (inGameUI is null)
            {
                return;
            }

            Toasts.Flush(inGameUI);

            var keyboard = __instance.TryGet<IInputManager>()?.Keyboard;
            if (keyboard is null)
            {
                return;
            }

            var pressed = KeyboardInputs.M.IsActive(keyboard) && KeyboardInputs.Control.IsActive(keyboard)
                && !KeyboardInputs.Alt.IsActive(keyboard) && !KeyboardInputs.Shift.IsActive(keyboard);
            if (pressed && !s_wasPressed)
            {
                MultiplayerScreen.Open();
            }

            s_wasPressed = pressed;
        }
        catch (Exception ex)
        {
            if (!s_failedOnce)
            {
                s_failedOnce = true;
                GameModule.Log.Warn("UI: the in-world hotkey handler failed; Ctrl+M is off this session.", ex);
            }
        }
    }
}
