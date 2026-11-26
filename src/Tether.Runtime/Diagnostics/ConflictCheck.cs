// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Linq;
using Tether.Diagnostics;

namespace Tether.Runtime.Diagnostics;

/// <summary>
/// Detects other multiplayer plugins. They patch the same game systems, so Tether will refuse to enable
/// networking alongside them. In this Phase 0 build it only warns.
/// </summary>
internal static class ConflictCheck
{
    private static readonly (string Assembly, string Name)[] KnownMultiplayerPlugins =
    {
        ("Y51_SE2Online", "SE2 Online"),
    };

    public static bool Run(ITetherLog log)
    {
        try
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var conflicts = KnownMultiplayerPlugins.Where(p => loaded.Contains(p.Assembly)).Select(p => p.Name).ToList();
            if (conflicts.Count == 0)
            {
                return false;
            }

            log.Warn($"Another multiplayer plugin is loaded: {string.Join(", ", conflicts)}. Tether will not enable networking alongside it. Remove one of them.");
            return true;
        }
        catch (Exception ex)
        {
            log.Warn("Conflict check failed.", ex);
            return false;
        }
    }
}
