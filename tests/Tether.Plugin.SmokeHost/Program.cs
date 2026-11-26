// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using Keen.VRage.Core.Plugins;

// Usage: Tether.Plugin.SmokeHost <path to Tether.dll> <seconds to keep running> [-tether:... options]
// The plugin reads its -tether: options from this process's command line, just as it would from the game's.
if (args.Length < 2 || !File.Exists(args[0]) || !int.TryParse(args[1], out var seconds))
{
    Console.Error.WriteLine("Usage: Tether.Plugin.SmokeHost <Tether.dll> <seconds> [-tether:options...]");
    return 1;
}

var pluginPath = Path.GetFullPath(args[0]);

// Load by path into the default context without enumerating types first: the strictest case for
// dependency resolution, since Tether.Core.dll and LiteNetLib.dll are not in this process's folder.
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginPath);
var pluginType = assembly.GetTypes().Single(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract);
Console.WriteLine($"smoke host: loaded {pluginType.FullName} from {pluginPath}");

// The game constructs PluginHost itself; the declaration has no public constructor, so make one without it.
var host = (PluginHost)RuntimeHelpers.GetUninitializedObject(typeof(PluginHost));
var plugin = (IPlugin)Activator.CreateInstance(pluginType, host)!;
Console.WriteLine($"smoke host: plugin constructed, running for {seconds} s");

Thread.Sleep(TimeSpan.FromSeconds(seconds));

(plugin as IDisposable)?.Dispose();
Console.WriteLine("smoke host: plugin disposed, exiting");
return 0;
