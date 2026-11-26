// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Tether.Plugin;

/// <summary>
/// Lets the runtime find Tether's own assemblies (Tether.Runtime, Tether.Core, LiteNetLib) next to Tether.dll.
/// The game loads plugins by path, and depending on how it does so the runtime may not look in the
/// plugin's folder by itself. Only assemblies that actually exist in our folder are served.
/// </summary>
/// <remarks>
/// This class must not reference any type outside the base library. It runs before those dependencies
/// can be resolved.
/// </remarks>
internal static class DependencyResolver
{
    private static string? s_directory;

    public static void Install(Assembly pluginAssembly)
    {
        if (s_directory is not null)
        {
            return;
        }

        var location = pluginAssembly.Location;
        if (string.IsNullOrEmpty(location))
        {
            // Loaded from memory (Pulsar compiles plugins itself and resolves their NuGet dependencies).
            s_directory = string.Empty;
            return;
        }

        s_directory = Path.GetDirectoryName(location) ?? string.Empty;
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (string.IsNullOrEmpty(s_directory) || string.IsNullOrEmpty(name.Name))
        {
            return null;
        }

        var candidate = Path.Combine(s_directory, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    }
}
