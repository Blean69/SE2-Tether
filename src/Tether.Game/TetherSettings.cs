// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using Keen.VRage.Core.Services.GameSessions;
using Tether.Diagnostics;

namespace Tether.Game;

/// <summary>Who may join a world you host.</summary>
internal enum HostVisibility
{
    /// <summary>Nobody can join. The game's own default.</summary>
    Hidden,

    /// <summary>Only people you invite through Steam.</summary>
    Private,

    /// <summary>Steam friends see "Join Game" when they right-click you.</summary>
    Friends,

    /// <summary>Anyone with the join code (and the session is findable by it).</summary>
    Public,
}

/// <param name="SafetyNet">
/// Keep the game running when one network message fails to process (and, where hooked, one component), logging
/// it instead of crashing. <c>-tether:safety=off</c> restores the game's own behaviour, for debugging.
/// </param>
internal sealed record TetherSettings(bool MultiplayerEnabled, HostVisibility Visibility, string? JoinCode, bool SafetyNet = true)
{
    public static readonly TetherSettings Default = new(true, HostVisibility.Hidden, null);

    /// <summary>
    /// Reads <c>-tether:mp=off</c>, <c>-tether:visibility=hidden|private|friends|public</c>,
    /// <c>-tether:join=CODE</c> and <c>-tether:safety=off</c>. The in-game panel will replace the visibility option; until then it is the
    /// way to open a world up.
    /// </summary>
    public static TetherSettings FromOptions(IReadOnlyDictionary<string, string?> options, ITetherLog log)
    {
        var enabled = !IsOff(options, "mp");
        var safetyNet = !IsOff(options, "safety");

        var visibility = Default.Visibility;
        if (options.TryGetValue("visibility", out var v) && !string.IsNullOrWhiteSpace(v))
        {
            visibility = v.Trim().ToLowerInvariant() switch
            {
                "hidden" or "off" => HostVisibility.Hidden,
                "private" or "invite" => HostVisibility.Private,
                "friends" or "friendsonly" => HostVisibility.Friends,
                "public" or "code" => HostVisibility.Public,
                _ => Unknown(v, log),
            };
        }

        options.TryGetValue("join", out var join);
        return new TetherSettings(enabled, visibility, string.IsNullOrWhiteSpace(join) ? null : join.Trim(), safetyNet);
    }

    private static bool IsOff(IReadOnlyDictionary<string, string?> options, string name) =>
        options.TryGetValue(name, out var value) && value is not null
        && (value.Equals("off", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase));

    public static JoinableSessionType ToGame(HostVisibility visibility) => visibility switch
    {
        HostVisibility.Private => JoinableSessionType.Private,
        HostVisibility.Friends => JoinableSessionType.FriendsOnly,
        HostVisibility.Public => JoinableSessionType.Public,
        _ => JoinableSessionType.Hidden,
    };

    public static string Describe(HostVisibility visibility) => visibility switch
    {
        HostVisibility.Private => "Private: only people you invite through Steam can join.",
        HostVisibility.Friends => "Friends: your Steam friends can right-click you and choose Join Game.",
        HostVisibility.Public => "Public: anyone with your join code can join.",
        _ => "Hidden: nobody can join.",
    };

    private static HostVisibility Unknown(string value, ITetherLog log)
    {
        log.Warn($"Unknown -tether:visibility '{value}'. Use hidden, private, friends or public. Using hidden.");
        return HostVisibility.Hidden;
    }
}
