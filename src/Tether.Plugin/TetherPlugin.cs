// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.IO;
using System.Runtime.CompilerServices;
using Keen.VRage.Core.Plugins;

namespace Tether.Plugin;

/// <summary>
/// Entry point. Space Engineers 2 constructs this when started with <c>-plugins:"&lt;path&gt;\Tether.dll"</c>.
/// Pulsar constructs it the same way.
/// </summary>
/// <remarks>
/// <para>
/// This assembly is a deliberately tiny loader shim. Besides the game's IPlugin and PluginHost it uses only
/// the base library, so the game can inspect or enumerate its types without needing any of Tether's
/// dependencies. It installs a resolver for Tether's own DLLs, then hands over to Tether.Runtime.
/// </para>
/// <para>Rule: nothing thrown here may crash the game. Every failure is logged and Tether disables itself.</para>
/// </remarks>
public sealed class TetherPlugin : IPlugin, IDisposable
{
    // Pulsar injects this static (name, subfolder) -> data path before constructing the plugin. Null when loaded via -plugins:.
#pragma warning disable CS0649, IDE0044 // assigned by Pulsar through reflection
    private static Func<string, string?, string>? GetConfigPath;
#pragma warning restore CS0649, IDE0044

    private IDisposable? _runtime;

    public TetherPlugin(PluginHost host)
    {
        try
        {
            DependencyResolver.Install(typeof(TetherPlugin).Assembly);
            _runtime = Start(host);
        }
        catch (Exception ex)
        {
            LogStartupFailure("Tether failed to start and has disabled itself.", ex);
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
            LogStartupFailure("Tether failed while unloading.", ex);
        }

        _runtime = null;
    }

    // Kept out of the constructor so Tether.Runtime is only resolved after the resolver is installed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IDisposable? Start(PluginHost host) => Tether.Runtime.PluginEntry.Start(host, GetConfigPath);

    /// <summary>Last-resort log, using only the base library. Same folder as Tether's normal log.</summary>
    private static void LogStartupFailure(string message, Exception exception)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
            var directory = Path.Combine(string.IsNullOrEmpty(appData) ? Path.GetTempPath() : appData, "SpaceEngineers2", "Tether");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "tether-startup-error.log"),
                $"{DateTime.UtcNow:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nothing else we can do without risking the game.
        }
    }
}
