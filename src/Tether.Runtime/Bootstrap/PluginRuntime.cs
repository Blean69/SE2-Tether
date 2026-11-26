// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.IO;
using Tether.Diagnostics;
using Tether.Runtime.Diagnostics;
using Tether.Protocol;

namespace Tether.Runtime.Bootstrap;

/// <summary>Everything Tether does inside the game process, created once the dependencies resolve.</summary>
internal sealed class PluginRuntime : IDisposable
{
    private const string LoadedKey = "Tether.Plugin.Loaded";

    private readonly PluginLog _log;
    private InGameNetworkTest? _networkTest;

    private PluginRuntime(PluginLog log)
    {
        _log = log;
    }

    /// <param name="host">The game's PluginHost, handled through reflection only.</param>
    public static PluginRuntime? Create(object host, Func<string, string?, string>? getConfigPath)
    {
        var self = typeof(PluginRuntime).Assembly.Location;
        if (AppDomain.CurrentDomain.GetData(LoadedKey) is string first)
        {
            // Both Pulsar and a -plugins: launch option can load us. A second copy must stay inert.
            EmergencyLog.Write($"Tether is already loaded from '{first}'; ignoring the copy at '{self}'.", new InvalidOperationException("Duplicate load"));
            return null;
        }

        AppDomain.CurrentDomain.SetData(LoadedKey, string.IsNullOrEmpty(self) ? "memory (Pulsar)" : self);

        var options = LaunchOptions.Parse(Environment.GetCommandLineArgs());
        var dataDirectory = options.Values.TryGetValue("data", out var dataOverride) && !string.IsNullOrWhiteSpace(dataOverride)
            ? Path.GetFullPath(dataOverride)
            : ResolveDataDirectory(getConfigPath);
        var log = new PluginLog(dataDirectory, options.Debug ? LogLevel.Debug : LogLevel.Info);
        var runtime = new PluginRuntime(log);

        log.Info($"Tether {TetherVersion.Plugin} (protocol {ProtocolConstants.ProtocolVersion}) loading. Unofficial, open source: GPL-3.0-or-later.");
        log.Info($"Data folder: {dataDirectory}");
        log.Info($"Loaded via: {(getConfigPath is not null ? "Pulsar" : "-plugins: launch option")}; mirroring to game log: {log.MirrorsToGameLog}.");

        EnvironmentReport.Write(log, typeof(PluginRuntime).Assembly);
        ConflictCheck.Run(log);
        HostEvents.Describe(host, log);

        HostEvents.TrySubscribe(host, "OnBeforeProjectsLoaded", arg => log.Info($"Lifecycle: OnBeforeProjectsLoaded ({HostEvents.Summarize(arg)})."), log);
        HostEvents.TrySubscribe(host, "OnBeforeEngineInstantiated", _ => runtime.OnBeforeEngineInstantiated(options, dataDirectory), log);

        try
        {
            runtime._networkTest = InGameNetworkTest.StartIfRequested(options, log, EnvironmentReport.DescribePlatform());
        }
        catch (Exception ex)
        {
            log.Error("Network self-test failed to start (is the port already in use?). The game is unaffected.", ex);
        }

        log.Info("Tether bootstrap complete. Phase 0 build: diagnostics only, no gameplay changes.");
        return runtime;
    }

    public void Dispose()
    {
        _log.Info("Tether unloading.");
        _networkTest?.Dispose();
        _networkTest = null;
        _log.Dispose();
    }

    private static string ResolveDataDirectory(Func<string, string?, string>? getConfigPath)
    {
        if (getConfigPath is not null)
        {
            try
            {
                var fromPulsar = getConfigPath("Tether", null);
                if (!string.IsNullOrWhiteSpace(fromPulsar))
                {
                    return fromPulsar;
                }
            }
            catch (Exception ex)
            {
                EmergencyLog.Write("Pulsar's GetConfigPath failed; using the default data folder.", ex);
            }
        }

        // Under Proton this is inside the game's Wine prefix: .../compatdata/1133870/pfx/drive_c/users/steamuser/AppData/Roaming.
        return EmergencyLog.DefaultDataDirectory();
    }

    private void OnBeforeEngineInstantiated(LaunchOptions options, string dataDirectory)
    {
        _log.Info("Lifecycle: OnBeforeEngineInstantiated.");
        if (options.DumpApiNamespaces is { } namespaces)
        {
            try
            {
                var written = ApiSurfaceDump.Write(Path.Combine(dataDirectory, "api-dumps"), namespaces, _log);
                _log.Info($"API surface dump: {written} type(s) written to {Path.Combine(dataDirectory, "api-dumps")}. Keep these files local; they describe Keen's code.");
            }
            catch (Exception ex)
            {
                _log.Error("API surface dump failed.", ex);
            }
        }
    }
}
