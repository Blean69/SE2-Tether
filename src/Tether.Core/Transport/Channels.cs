// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

namespace Tether.Transport;

/// <summary>How a transport must deliver messages on a channel.</summary>
public enum Delivery : byte
{
    /// <summary>May be lost; arrives in order, and late (older) messages are dropped.</summary>
    UnreliableSequenced,

    /// <summary>Never lost; arrives in send order relative to the same channel.</summary>
    ReliableOrdered,
}

/// <summary>
/// The fixed channel table every transport implements. Channels order independently, so
/// a large world-transfer chunk never delays control or replication traffic.
/// </summary>
public static class Channels
{
    /// <summary>Handshake, player list, and chat.</summary>
    public const byte Control = 0;

    /// <summary>Game-engine data that must arrive, such as replication events.</summary>
    public const byte EngineReliable = 1;

    /// <summary>Game-engine state where only the latest matters, such as movement snapshots.</summary>
    public const byte EngineUnreliable = 2;

    /// <summary>Large transfers, such as world snapshots, chunked by the sender.</summary>
    public const byte Bulk = 3;

    public const byte Count = 4;

    public static bool IsValid(byte channel) => channel < Count;

    public static Delivery DeliveryOf(byte channel) =>
        channel == EngineUnreliable ? Delivery.UnreliableSequenced : Delivery.ReliableOrdered;
}
