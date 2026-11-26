// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

namespace Tether.Protocol;

/// <summary>
/// Why a connection ended. Transports carry the code to the other side where they can.
/// </summary>
/// <remarks>Values are part of the wire format: never renumber, only append.</remarks>
public enum DisconnectReason : byte
{
    Unknown = 0,

    // Transport-level outcomes.
    ConnectionFailed = 1,
    Timeout = 2,
    TransportError = 3,

    // Session-level outcomes, sent deliberately by one side.
    ClientLeft = 10,
    HostShutdown = 11,
    ProtocolMismatch = 12,
    GameBuildMismatch = 13,
    ModMismatch = 14,
    SessionFull = 15,
    WrongPassword = 16,
    PasswordRequired = 17,
    Kicked = 18,
    HandshakeTimeout = 19,
    ProtocolViolation = 20,
    RateLimited = 21,
}

public static class DisconnectReasonText
{
    /// <summary>A short, player-facing explanation.</summary>
    public static string Describe(DisconnectReason reason) => reason switch
    {
        DisconnectReason.ConnectionFailed => "Could not connect to the host.",
        DisconnectReason.Timeout => "The connection timed out.",
        DisconnectReason.TransportError => "A network error occurred.",
        DisconnectReason.ClientLeft => "The player left.",
        DisconnectReason.HostShutdown => "The host ended the session.",
        DisconnectReason.ProtocolMismatch => "Tether versions are incompatible. Everyone needs the same Tether version.",
        DisconnectReason.GameBuildMismatch => "Game versions differ. Everyone needs the same Space Engineers 2 build.",
        DisconnectReason.ModMismatch => "Mod lists differ. Everyone needs the same mods.",
        DisconnectReason.SessionFull => "The session is full.",
        DisconnectReason.WrongPassword => "Wrong password.",
        DisconnectReason.PasswordRequired => "This session requires a password.",
        DisconnectReason.Kicked => "You were kicked by the host.",
        DisconnectReason.HandshakeTimeout => "Joining took too long.",
        DisconnectReason.ProtocolViolation => "Received invalid data.",
        DisconnectReason.RateLimited => "Too much traffic was sent.",
        _ => "Disconnected.",
    };
}
