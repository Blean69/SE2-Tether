// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

namespace Tether.Protocol;

/// <summary>
/// First byte of every transport message.
/// </summary>
/// <remarks>
/// <para>Values are part of the wire format: never renumber, only append.</para>
/// <para>
/// The layout of <see cref="Hello"/> up to and including its protocol version field is frozen forever.
/// That way any future version can still read it and refuse cleanly with a version mismatch.
/// </para>
/// </remarks>
public enum MessageType : byte
{
    /// <summary>Client → host. The first message on a new connection.</summary>
    Hello = 1,

    /// <summary>Host → client. Asks for proof of the session password.</summary>
    AuthChallenge = 2,

    /// <summary>Client → host. Proof of the session password.</summary>
    AuthResponse = 3,

    /// <summary>Host → client. The join succeeded, and carries the client's id and the player list.</summary>
    Welcome = 4,

    /// <summary>Host → clients.</summary>
    PlayerJoined = 5,

    /// <summary>Host → clients.</summary>
    PlayerLeft = 6,

    /// <summary>Client → host. A chat line typed by the client.</summary>
    ChatSend = 7,

    /// <summary>Host → clients. A chat line attributed by the host to a player.</summary>
    ChatMessage = 8,

    /// <summary>Either direction, only after joining. An opaque game-engine payload, such as replication data.</summary>
    EngineData = 32,
}
