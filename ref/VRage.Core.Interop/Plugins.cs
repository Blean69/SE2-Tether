// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

// Interop declarations written by Tether contributors. They name two Space Engineers 2 types, matching the
// names public plugins use with the game's built-in -plugins: launch option. They contain no game code.
// At runtime the game's own VRage.Core assembly provides the real types.

namespace Keen.VRage.Core.Plugins;

/// <summary>Marker interface the game's plugin loader looks for.</summary>
public interface IPlugin
{
}

/// <summary>
/// Passed to a plugin's constructor. Tether reaches its members through reflection (see HostEvents in
/// Tether.Plugin), so no members are declared here.
/// </summary>
public sealed class PluginHost
{
    private PluginHost()
    {
    }
}
