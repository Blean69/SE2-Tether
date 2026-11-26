// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Tether.Diagnostics;

namespace Tether.Runtime.Modules;

/// <summary>
/// A feature module compiled against the real game (Tether.Game.dll). Tether.Runtime itself never
/// references game assemblies, so it builds anywhere; it loads a module only if one is shipped.
/// </summary>
public interface ITetherGameModule : IDisposable
{
    /// <summary>Called once, while the plugin starts, before the game builds its engine.</summary>
    void Start(GameModuleContext context);

    /// <summary>Called from the game's OnBeforeEngineInstantiated plugin event.</summary>
    void OnBeforeEngineInstantiated();
}

public sealed class GameModuleContext
{
    public GameModuleContext(ITetherLog log, IReadOnlyDictionary<string, string?> options, string dataDirectory)
    {
        Log = log;
        Options = options;
        DataDirectory = dataDirectory;
    }

    public ITetherLog Log { get; }

    /// <summary>The -tether: launch options, keyed without the prefix.</summary>
    public IReadOnlyDictionary<string, string?> Options { get; }

    public string DataDirectory { get; }
}

internal static class GameModuleLoader
{
    private const string AssemblyName = "Tether.Game";

    /// <summary>Finds and starts the game module, or returns null (logged) when it isn't present or fails.</summary>
    public static ITetherGameModule? TryStart(GameModuleContext context)
    {
        var log = context.Log;
        Assembly? assembly = null;

        // Pulsar compiles every source directory into one in-memory assembly: look there first.
        var self = typeof(GameModuleLoader).Assembly;
        if (FindModuleType(self) is not null)
        {
            assembly = self;
        }
        else
        {
            try
            {
                assembly = Assembly.Load(new AssemblyName(AssemblyName));
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                log.Info("Game module (Tether.Game.dll) not present: in-game multiplayer features are off in this build.");
                return null;
            }
        }

        var type = FindModuleType(assembly);
        if (type is null)
        {
            log.Warn($"{assembly.GetName().Name} contains no game module.");
            return null;
        }

        try
        {
            var module = (ITetherGameModule)Activator.CreateInstance(type)!;
            module.Start(context);
            log.Info($"Game module started ({assembly.GetName().Name} {assembly.GetName().Version}).");
            return module;
        }
        catch (Exception ex)
        {
            log.Error("Game module failed to start; in-game multiplayer features are off. The game is unaffected.", ex);
            return null;
        }
    }

    private static Type? FindModuleType(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().FirstOrDefault(t => !t.IsAbstract && typeof(ITetherGameModule).IsAssignableFrom(t));
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.FirstOrDefault(t => t is not null && !t.IsAbstract && typeof(ITetherGameModule).IsAssignableFrom(t));
        }
    }
}
