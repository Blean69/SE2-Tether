// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Tether.Diagnostics;

namespace Tether.Runtime.Diagnostics;

/// <summary>
/// Logs the facts Phase 0 needs: game build, runtime, platform (Windows, Proton/Wine, or native Linux),
/// and the startup settings that affect patching.
/// </summary>
internal static class EnvironmentReport
{
    public static void Write(ITetherLog log, Assembly pluginAssembly)
    {
        Safe(log, "plugin", () =>
        {
            // Where each of Tether's assemblies was loaded from shows whether dependency resolution worked.
            var ours = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name is "Tether" or "Tether.Runtime" or "Tether.Core" or "LiteNetLib")
                .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
                .Select(a => $"{a.GetName().Name} {a.GetName().Version} @ {(string.IsNullOrEmpty(a.Location) ? "in memory (Pulsar)" : a.Location)}");
            log.Info($"Tether assemblies: {string.Join("; ", ours)}");
        });

        Safe(log, "runtime", () =>
        {
            log.Info($"Runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.ProcessArchitecture}, OS: {RuntimeInformation.OSDescription}");
            log.Info($"Platform: {DescribePlatform()}");
            var compatData = Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH");
            if (compatData is not null)
            {
                log.Info($"Proton compat data folder: {compatData}");
            }
        });

        Safe(log, "game", () =>
        {
            var entry = Assembly.GetEntryAssembly();
            var info = entry?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            log.Info($"Entry assembly: {entry?.GetName().Name} {entry?.GetName().Version} (informational: {info ?? "n/a"})");

            var baseDir = AppContext.BaseDirectory;
            var gameDll = Path.Combine(baseDir, "SpaceEngineers2.dll");
            if (File.Exists(gameDll))
            {
                var version = FileVersionInfo.GetVersionInfo(gameDll);
                log.Info($"SpaceEngineers2.dll file version: {version.FileVersion}, product version: {version.ProductVersion}");
            }

            log.Info($"Game folder: {baseDir}");
            var runtimeConfig = Path.Combine(baseDir, "SpaceEngineers2.runtimeconfig.json");
            if (File.Exists(runtimeConfig))
            {
                var text = File.ReadAllText(runtimeConfig);
                log.Info($"SpaceEngineers2.runtimeconfig.json: {(text.Length > 2000 ? text.Substring(0, 2000) + "..." : text).Replace(Environment.NewLine, " ")}");
            }
        });

        Safe(log, "startup settings", () =>
        {
            foreach (var name in new[] { "DOTNET_ReadyToRun", "DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_STARTUP_HOOKS" })
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (value is not null)
                {
                    log.Info($"Env {name}={value}");
                }
            }

            var hooks = AppContext.TryGetSwitch("System.StartupHookProvider.IsSupported", out var supported) ? supported.ToString() : "not set";
            log.Info($"Startup hooks switch: {hooks}");
        });

        Safe(log, "assemblies", () =>
        {
            var loaded = AppDomain.CurrentDomain.GetAssemblies();
            var steam = loaded.FirstOrDefault(a => a.GetName().Name is "Steamworks.NET" or "com.rlabrecque.steamworks.net");
            log.Info($"Loaded assemblies: {loaded.Length}; Steamworks: {(steam is null ? "not loaded yet" : steam.GetName().Name + " " + steam.GetName().Version)}");
        });
    }

    /// <summary>
    /// A short platform name such as "Windows build under Wine/Proton". It never contains paths or user
    /// names, because the network self-test shares it with other players.
    /// </summary>
    public static string DescribePlatform()
    {
        if (OperatingSystem.IsLinux())
        {
            return "native Linux (e.g. Pulsar + LinuxCompat)";
        }

        if (!OperatingSystem.IsWindows())
        {
            return "other: " + RuntimeInformation.OSDescription;
        }

        var wine = false;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\Wine");
            wine = key is not null;
        }
        catch
        {
        }

        var compatData = Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH");
        if (wine || compatData is not null)
        {
            return compatData is not null ? "Windows build under Wine/Proton" : "Windows build under Wine";
        }

        return "Windows";
    }

    private static void Safe(ITetherLog log, string section, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            log.Warn($"Environment report section '{section}' failed.", ex);
        }
    }
}
