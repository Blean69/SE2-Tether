// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Runtime.CompilerServices;
using Keen.VRage.Core.Plugins;
using Tether.Plugin.Bootstrap;

namespace Tether.Plugin;

/// <summary>
/// Entry point. Space Engineers 2 constructs this when started with <c>-plugins:"&lt;path&gt;\Tether.dll"</c>.
/// Pulsar constructs it the same way.
/// </summary>
/// <remarks>
/// <para>
/// This is the Phase 0 bootstrap. It proves the plugin loads on Windows, Proton and native Linux,
/// records the environment, and can dump the game's networking API surface to help map Keen's transport.
/// It does not change gameplay yet.
/// </para>
/// <para>
/// Rule: nothing thrown here may crash the game. Every failure is logged and Tether disables itself.
/// </para>
/// </remarks>
public sealed class TetherPlugin : IPlugin, IDisposable
{
    // Pulsar injects this static (name, subfolder) -> data path before constructing the plugin. Null when loaded via -plugins:.
#pragma warning disable CS0649, IDE0044 // assigned by Pulsar through reflection
    private static Func<string, string?, string>? GetConfigPath;
#pragma warning restore CS0649, IDE0044

    private PluginRuntime? _runtime;

    public TetherPlugin(PluginHost host)
    {
        try
        {
            // Must run before any method that touches Tether.Core or other dependencies is JIT-compiled.
            DependencyResolver.Install(typeof(TetherPlugin).Assembly);
            Start(host);
        }
        catch (Exception ex)
        {
            EmergencyLog.Write("Tether failed to start and has disabled itself.", ex);
        }
    }

    public void Dispose()
    {
        try
        {
            _runtime?.Dispose();
        }
        catch (Exception ex)
        {
            EmergencyLog.Write("Tether failed while unloading.", ex);
        }

        _runtime = null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Start(PluginHost host) => _runtime = PluginRuntime.Create(host, GetConfigPath);
}
