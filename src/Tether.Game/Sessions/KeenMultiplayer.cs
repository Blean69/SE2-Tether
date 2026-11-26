// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using Keen.Game2.Simulation;
using Tether.Diagnostics;

namespace Tether.Game.Sessions;

/// <summary>The game's own switch for its built-in (not yet released) multiplayer.</summary>
internal static class KeenMultiplayer
{
    public static void Enable(ITetherLog log)
    {
        // Per Keen's docs: "When true, locally hosted worlds are advertised to Steam friends and incoming join
        // requests are accepted." Same effect as the hidden -enableMultiplayer launch option. Who may actually
        // join is still controlled by the session's visibility, which Tether keeps at the player's choice.
        if (!GameFeatureConfig.MultiplayerEnabled)
        {
            GameFeatureConfig.MultiplayerEnabled = true;
            log.Info("Multiplayer: switched on the game's built-in multiplayer.");
        }
    }
}
