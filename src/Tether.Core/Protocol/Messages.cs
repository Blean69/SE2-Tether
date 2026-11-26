// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;

namespace Tether.Protocol;

// Every message is: [MessageType byte][body]. The WriteTo methods emit the type byte. The ReadBody
// methods expect the caller to have consumed it already, and they reject trailing bytes.

/// <summary>Client → host. The first message on a connection.</summary>
public sealed record HelloMessage(
    ushort ProtocolVersion,
    string PluginVersion,
    string GameBuild,
    byte[] ModFingerprint,
    string PlayerName,
    PeerCapabilities Capabilities)
{
    public void WriteTo(PacketWriter writer)
    {
        writer.WriteByte((byte)MessageType.Hello);
        // Frozen prefix: the protocol version always comes first (see MessageType remarks).
        writer.WriteUInt16(ProtocolVersion);
        writer.WriteString(PluginVersion, ProtocolConstants.MaxVersionStringBytes);
        writer.WriteString(GameBuild, ProtocolConstants.MaxGameBuildBytes);
        writer.WriteBytes(ModFingerprint, ProtocolConstants.MaxModFingerprintBytes);
        writer.WriteString(PlayerName, ProtocolConstants.MaxPlayerNameBytes);
        writer.WriteUInt32((uint)Capabilities);
    }

    /// <summary>Reads only the frozen version prefix, so mismatched peers can be refused without parsing the rest.</summary>
    public static ushort ReadProtocolVersion(ref PacketReader reader) => reader.ReadUInt16();

    /// <summary>Reads the rest of the body after <see cref="ReadProtocolVersion"/>.</summary>
    public static HelloMessage ReadRemainder(ref PacketReader reader, ushort protocolVersion)
    {
        var pluginVersion = reader.ReadString(ProtocolConstants.MaxVersionStringBytes);
        var gameBuild = reader.ReadString(ProtocolConstants.MaxGameBuildBytes);
        var fingerprint = reader.ReadBytes(ProtocolConstants.MaxModFingerprintBytes).ToArray();
        var playerName = reader.ReadString(ProtocolConstants.MaxPlayerNameBytes);
        var capabilities = (PeerCapabilities)reader.ReadUInt32();
        reader.EnsureEnd();
        return new HelloMessage(protocolVersion, pluginVersion, gameBuild, fingerprint, playerName, capabilities);
    }
}

/// <summary>Host → client. Prove you know the session password.</summary>
public sealed record AuthChallengeMessage(byte[] Nonce, byte[] Salt, int Iterations)
{
    public void WriteTo(PacketWriter writer)
    {
        writer.WriteByte((byte)MessageType.AuthChallenge);
        writer.WriteRaw(Nonce);
        writer.WriteRaw(Salt);
        writer.WriteVarUInt32((uint)Iterations);
    }

    public static AuthChallengeMessage ReadBody(ref PacketReader reader)
    {
        var nonce = reader.ReadRaw(ProtocolConstants.AuthNonceSize).ToArray();
        var salt = reader.ReadRaw(ProtocolConstants.AuthSaltSize).ToArray();
        var iterations = reader.ReadVarUInt32();
        if (iterations == 0 || iterations > ProtocolConstants.MaxPasswordIterations)
        {
            throw new ProtocolViolationException($"Password iteration count {iterations} is out of range.");
        }

        reader.EnsureEnd();
        return new AuthChallengeMessage(nonce, salt, (int)iterations);
    }
}

/// <summary>Client → host. A MAC over the challenge nonce, keyed by the password-derived key.</summary>
public sealed record AuthResponseMessage(byte[] Mac)
{
    public void WriteTo(PacketWriter writer)
    {
        writer.WriteByte((byte)MessageType.AuthResponse);
        writer.WriteRaw(Mac);
    }

    public static AuthResponseMessage ReadBody(ref PacketReader reader)
    {
        var mac = reader.ReadRaw(ProtocolConstants.AuthMacSize).ToArray();
        reader.EnsureEnd();
        return new AuthResponseMessage(mac);
    }
}

/// <summary>Host → client. The join succeeded.</summary>
public sealed record WelcomeMessage(PlayerId YourId, string SessionName, byte MaxPlayers, IReadOnlyList<PlayerInfo> Players)
{
    public void WriteTo(PacketWriter writer)
    {
        writer.WriteByte((byte)MessageType.Welcome);
        writer.WriteByte(YourId.Value);
        writer.WriteString(SessionName, ProtocolConstants.MaxSessionNameBytes);
        writer.WriteByte(MaxPlayers);
        writer.WriteVarUInt32((uint)Players.Count);
        foreach (var player in Players)
        {
            player.WriteTo(writer);
        }
    }

    public static WelcomeMessage ReadBody(ref PacketReader reader)
    {
        var yourId = new PlayerId(reader.ReadByte());
        var sessionName = reader.ReadString(ProtocolConstants.MaxSessionNameBytes);
        var maxPlayers = reader.ReadByte();
        if (maxPlayers < 2 || maxPlayers > ProtocolConstants.MaxPlayers)
        {
            throw new ProtocolViolationException($"Max players {maxPlayers} is out of range.");
        }

        var count = reader.ReadLength(maxPlayers);
        var players = new List<PlayerInfo>(count);
        var seen = new HashSet<byte>();
        for (var i = 0; i < count; i++)
        {
            var player = PlayerInfo.ReadFrom(ref reader);
            if (!seen.Add(player.Id.Value))
            {
                throw new ProtocolViolationException($"Duplicate player id {player.Id} in welcome.");
            }

            players.Add(player);
        }

        if (!yourId.IsValid || !seen.Contains(yourId.Value))
        {
            throw new ProtocolViolationException("Welcome does not list the receiving player.");
        }

        reader.EnsureEnd();
        return new WelcomeMessage(yourId, sessionName, maxPlayers, players);
    }
}

/// <summary>Host → clients.</summary>
public sealed record PlayerJoinedMessage(PlayerInfo Player)
{
    public void WriteTo(PacketWriter writer)
    {
        writer.WriteByte((byte)MessageType.PlayerJoined);
        Player.WriteTo(writer);
    }

    public static PlayerJoinedMessage ReadBody(ref PacketReader reader)
    {
        var player = PlayerInfo.ReadFrom(ref reader);
        reader.EnsureEnd();
        return new PlayerJoinedMessage(player);
    }
}

/// <summary>Host → clients.</summary>
public sealed record PlayerLeftMessage(PlayerId Player, DisconnectReason Reason)
{
    public void WriteTo(PacketWriter writer)
    {
        writer.WriteByte((byte)MessageType.PlayerLeft);
        writer.WriteByte(Player.Value);
        writer.WriteByte((byte)Reason);
    }

    public static PlayerLeftMessage ReadBody(ref PacketReader reader)
    {
        var player = new PlayerId(reader.ReadByte());
        if (!player.IsValid)
        {
            throw new ProtocolViolationException("Player id 0 is not valid.");
        }

        var reason = (DisconnectReason)reader.ReadByte();
        reader.EnsureEnd();
        return new PlayerLeftMessage(player, reason);
    }
}

/// <summary>Client → host. A chat line. The host decides who it is from, so clients can't impersonate.</summary>
public sealed record ChatSendMessage(string Text)
{
    public void WriteTo(PacketWriter writer)
    {
        writer.WriteByte((byte)MessageType.ChatSend);
        writer.WriteString(Text, ProtocolConstants.MaxChatBytes);
    }

    public static ChatSendMessage ReadBody(ref PacketReader reader)
    {
        var text = reader.ReadString(ProtocolConstants.MaxChatBytes);
        reader.EnsureEnd();
        return new ChatSendMessage(text);
    }
}

/// <summary>Host → clients. A chat line, with its sender set by the host.</summary>
public sealed record ChatMessage(PlayerId From, string Text)
{
    public void WriteTo(PacketWriter writer)
    {
        writer.WriteByte((byte)MessageType.ChatMessage);
        writer.WriteByte(From.Value);
        writer.WriteString(Text, ProtocolConstants.MaxChatBytes);
    }

    public static ChatMessage ReadBody(ref PacketReader reader)
    {
        var from = new PlayerId(reader.ReadByte());
        var text = reader.ReadString(ProtocolConstants.MaxChatBytes);
        reader.EnsureEnd();
        return new ChatMessage(from, text);
    }
}

/// <summary>Helpers for <see cref="MessageType.EngineData"/>, which is not a record because its payload is passed through without copying.</summary>
public static class EngineDataMessage
{
    public static void WriteTo(PacketWriter writer, ReadOnlySpan<byte> payload)
    {
        writer.WriteByte((byte)MessageType.EngineData);
        writer.WriteRaw(payload);
    }
}
