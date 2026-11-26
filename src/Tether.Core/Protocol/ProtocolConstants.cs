// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

namespace Tether.Protocol;

/// <summary>
/// Wire-level constants and limits that both ends must agree on.
/// </summary>
public static class ProtocolConstants
{
    /// <summary>
    /// Bump on <b>any</b> change to the wire format: new messages, new fields, or changed meanings.
    /// Peers with different versions refuse each other cleanly during the handshake.
    /// </summary>
    public const ushort ProtocolVersion = 1;

    /// <summary>Hard protocol cap on players per session, host included. Sessions default to far fewer (see SessionDefaults).</summary>
    public const int MaxPlayers = 16;

    public const int MaxPlayerNameChars = 32;
    public const int MaxPlayerNameBytes = MaxPlayerNameChars * 4;

    public const int MaxSessionNameChars = 64;
    public const int MaxSessionNameBytes = MaxSessionNameChars * 4;

    public const int MaxChatChars = 256;
    public const int MaxChatBytes = MaxChatChars * 4;

    public const int MaxVersionStringBytes = 64;
    public const int MaxGameBuildBytes = 64;
    public const int MaxModFingerprintBytes = 64;

    /// <summary>Limit for the human-readable text carried with a disconnect.</summary>
    public const int MaxDisconnectMessageBytes = 200;

    /// <summary>Largest single reliable message any transport must accept. Bigger payloads are chunked by the sender.</summary>
    public const int MaxReliablePayload = 256 * 1024;

    public const int AuthNonceSize = 32;
    public const int AuthSaltSize = 16;
    public const int AuthMacSize = 32;

    /// <summary>Clients refuse challenges above this, so a hostile host can't make them burn CPU.</summary>
    public const int MaxPasswordIterations = 1_000_000;
    public const int DefaultPasswordIterations = 100_000;
}
