// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using Tether.Runtime.Bootstrap;

namespace Tether.Runtime;

/// <summary>The one entry point the loader shim (Tether.dll) calls.</summary>
public static class PluginEntry
{
    /// <summary>Starts Tether. Returns what to dispose when the game unloads the plugin, or null if Tether stays inert.</summary>
    /// <param name="pluginHost">The game's PluginHost instance (used through reflection).</param>
    /// <param name="getConfigPath">Pulsar's injected data-path function, or null when loaded via -plugins:.</param>
    public static IDisposable? Start(object pluginHost, Func<string, string?, string>? getConfigPath)
    {
        ArgumentNullException.ThrowIfNull(pluginHost);
        return PluginRuntime.Create(pluginHost, getConfigPath);
    }
}
