// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;

namespace Tether.Protocol;

/// <summary>Session-scoped player number. Assigned by the host; 1 is always the host.</summary>
public readonly record struct PlayerId(byte Value)
{
    public static readonly PlayerId None = new(0);

    public static readonly PlayerId Host = new(1);

    public bool IsValid => Value != 0;

    public override string ToString() => $"#{Value}";
}

/// <summary>What every participant knows about a player. Deliberately excludes addresses and platform ids, for privacy.</summary>
public sealed record PlayerInfo(PlayerId Id, string Name)
{
    internal void WriteTo(PacketWriter writer)
    {
        writer.WriteByte(Id.Value);
        writer.WriteString(Name, ProtocolConstants.MaxPlayerNameBytes);
    }

    internal static PlayerInfo ReadFrom(ref PacketReader reader)
    {
        var id = new PlayerId(reader.ReadByte());
        if (!id.IsValid)
        {
            throw new ProtocolViolationException("Player id 0 is not valid.");
        }

        var name = reader.ReadString(ProtocolConstants.MaxPlayerNameBytes);
        if (name.Length == 0 || name.Length > ProtocolConstants.MaxPlayerNameChars)
        {
            throw new ProtocolViolationException("Player name length is out of range.");
        }

        return new PlayerInfo(id, name);
    }
}

/// <summary>Reserved for negotiating optional features without bumping the protocol version.</summary>
[Flags]
public enum PeerCapabilities : uint
{
    None = 0,
}
