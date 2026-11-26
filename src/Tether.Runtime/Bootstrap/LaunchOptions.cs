// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;

namespace Tether.Runtime.Bootstrap;

/// <summary>
/// Tether's own launch options, written as <c>-tether:&lt;name&gt;[=&lt;value&gt;]</c> next to <c>-plugins:</c>
/// in Steam's launch options.
/// </summary>
/// <example>
/// <c>-tether:debug</c> logs at debug level.
/// <c>-tether:dump-api</c> writes the networking API surface to the data folder.
/// <c>-tether:dump-api=Keen.VRage.Multiplayer,Keen.Game2.Game.Networking</c> chooses the namespaces.
/// <c>-tether:host-test[=port]</c> or <c>-tether:join-test=host[:port]</c> runs the network self-test
/// (see InGameNetworkTest), with optional <c>-tether:name=...</c> and <c>-tether:password=...</c>.
/// <c>-tether:data=&lt;folder&gt;</c> puts Tether's log and other files in that folder instead of the default.
/// </example>
internal sealed class LaunchOptions
{
    public const string Prefix = "-tether:";

    public static readonly string[] DefaultDumpNamespaces =
    {
        "Keen.VRage.Multiplayer",
        "Keen.Game2.Game.Networking",
        "Keen.Game2.Game.SessionComponents",
        "Keen.VRage.Core.Plugins",
        "Keen.VRage.Core.Services.Networking",
        "Keen.VRage.Core.Services.GameSessions",
    };

    private LaunchOptions(Dictionary<string, string?> values, IReadOnlyList<string> rawArgs)
    {
        Values = values;
        RawArgs = rawArgs;
    }

    public IReadOnlyDictionary<string, string?> Values { get; }

    public IReadOnlyList<string> RawArgs { get; }

    public bool Debug => Values.ContainsKey("debug");

    /// <summary>Namespaces to dump, or null when the dump wasn't requested.</summary>
    public IReadOnlyList<string>? DumpApiNamespaces =>
        Values.TryGetValue("dump-api", out var value)
            ? string.IsNullOrWhiteSpace(value) ? DefaultDumpNamespaces : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;

    public static LaunchOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            if (!arg.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var body = arg.Substring(Prefix.Length);
            var eq = body.IndexOf('=');
            if (eq < 0)
            {
                values[body.Trim()] = null;
            }
            else
            {
                values[body.Substring(0, eq).Trim()] = body.Substring(eq + 1).Trim();
            }
        }

        return new LaunchOptions(values, args);
    }
}
