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

internal sealed record TetherSettings(bool MultiplayerEnabled, HostVisibility Visibility, string? JoinCode)
{
    public static readonly TetherSettings Default = new(true, HostVisibility.Hidden, null);

    /// <summary>
    /// Reads <c>-tether:mp=off</c>, <c>-tether:visibility=hidden|private|friends|public</c> and
    /// <c>-tether:join=CODE</c>. The in-game panel will replace the visibility option; until then it is the
    /// way to open a world up.
    /// </summary>
    public static TetherSettings FromOptions(IReadOnlyDictionary<string, string?> options, ITetherLog log)
    {
        var enabled = !(options.TryGetValue("mp", out var mp) && mp is not null
            && (mp.Equals("off", StringComparison.OrdinalIgnoreCase) || mp.Equals("false", StringComparison.OrdinalIgnoreCase)));

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
        return new TetherSettings(enabled, visibility, string.IsNullOrWhiteSpace(join) ? null : join.Trim());
    }

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
