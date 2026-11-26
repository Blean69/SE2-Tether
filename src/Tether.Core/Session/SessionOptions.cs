// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Tether.Protocol;

namespace Tether.Session;

public static class SessionDefaults
{
    /// <summary>v1 targets small co-op sessions. The host counts as one of the players.</summary>
    public const int MaxPlayers = 4;
}

/// <summary>What must match between host and client for the game state to be compatible.</summary>
public sealed record GameIdentity(string GameBuild, byte[] ModFingerprint)
{
    public static readonly GameIdentity Unknown = new(string.Empty, Array.Empty<byte>());

    /// <summary>
    /// A stable fingerprint of a mod list. Order and duplicates don't matter. Ids should include a
    /// version where available, e.g. "workshop:123456@2026-11-20".
    /// </summary>
    public static byte[] ComputeModFingerprint(IEnumerable<string> modIds)
    {
        var canonical = string.Join("\n", modIds.Select(id => id.Trim()).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal));
        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
    }

    public bool IsCompatibleBuild(string otherBuild) => string.Equals(GameBuild, otherBuild, StringComparison.Ordinal);

    public bool IsCompatibleMods(ReadOnlySpan<byte> otherFingerprint) => ModFingerprint.AsSpan().SequenceEqual(otherFingerprint);
}

/// <summary>Per-client traffic budgets enforced by the host. Exceeding them disconnects the client.</summary>
public sealed record RateLimits
{
    public static readonly RateLimits Default = new();

    public int MessagesPerSecond { get; init; } = 400;

    public int MessageBurst { get; init; } = 800;

    public int BytesPerSecond { get; init; } = 1024 * 1024;

    public int ByteBurst { get; init; } = 4 * 1024 * 1024;

    /// <summary>Chat over budget is dropped, not punished.</summary>
    public double ChatPerSecond { get; init; } = 1;

    public int ChatBurst { get; init; } = 5;
}

public sealed record HostOptions
{
    public string SessionName { get; init; } = "Tether session";

    public string HostPlayerName { get; init; } = "Host";

    /// <summary>Including the host. From 2 to <see cref="ProtocolConstants.MaxPlayers"/>.</summary>
    public int MaxPlayers { get; init; } = SessionDefaults.MaxPlayers;

    /// <summary>Optional shared password. Null or empty means anyone with the address can join.</summary>
    public string? Password { get; init; }

    public GameIdentity Game { get; init; } = GameIdentity.Unknown;

    public int HandshakeTimeoutMs { get; init; } = 15_000;

    public int PasswordIterations { get; init; } = ProtocolConstants.DefaultPasswordIterations;

    public RateLimits ClientLimits { get; init; } = RateLimits.Default;
}

public sealed record ClientOptions
{
    public string PlayerName { get; init; } = "Player";

    public string? Password { get; init; }

    public GameIdentity Game { get; init; } = GameIdentity.Unknown;

    /// <summary>Covers connecting and the whole handshake.</summary>
    public int JoinTimeoutMs { get; init; } = 30_000;
}

/// <summary>Receives opaque game-engine payloads. <paramref name="payload"/> is only valid during the call.</summary>
public delegate void EngineDataHandler(PlayerId from, byte channel, ReadOnlySpan<byte> payload);
