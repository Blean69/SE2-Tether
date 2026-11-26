// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using Tether.Diagnostics;
using System.Collections.Concurrent;
using Keen.Game2.Client.UI.InGame;
using Keen.Game2.Client.UI.Shared.SystemNotification;

namespace Tether.Game.UI;

/// <summary>
/// Small notifications in the game's own toast style. Messages raised before the in-world UI exists (for
/// example while a world loads) are queued and shown once it does.
/// </summary>
internal static class Toasts
{
    private static readonly ConcurrentQueue<(string Title, string Text)> Pending = new();

    public static void Show(string text, string title = "Tether multiplayer")
    {
        Pending.Enqueue((title, text));
        GameModule.Log.Debug($"Toast queued: {text}");
    }

    /// <summary>Called every frame from the client session's update.</summary>
    public static void Flush(SessionInGameUISessionComponent inGameUI)
    {
        while (Pending.TryDequeue(out var toast))
        {
            try
            {
                inGameUI.DisplayNotification(new NotificationViewModel(title: toast.Title, content: toast.Text, timeout: TimeSpan.FromSeconds(8), icon: null));
            }
            catch (Exception ex)
            {
                GameModule.Log.Debug($"Toast could not be shown: {ex.Message}");
            }
        }
    }
}
