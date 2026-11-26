// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using Tether.Protocol;
using Xunit;

namespace Tether.Tests.Protocol;

public class MessageTests
{
    private delegate T BodyReader<T>(ref PacketReader reader);

    [Fact]
    public void Hello_RoundTrips_AndVersionIsTheFrozenPrefix()
    {
        var hello = new HelloMessage(ProtocolConstants.ProtocolVersion, "0.1.0", "4.0.0.3169", new byte[] { 9, 8, 7 }, "Ripley", PeerCapabilities.None);
        var bytes = Encode(hello.WriteTo);

        Assert.Equal((byte)MessageType.Hello, bytes[0]);
        Assert.Equal(ProtocolConstants.ProtocolVersion, BitConverter.ToUInt16(bytes, 1)); // little-endian, right after the type

        var reader = new PacketReader(bytes);
        reader.ReadByte();
        var version = HelloMessage.ReadProtocolVersion(ref reader);
        var decoded = HelloMessage.ReadRemainder(ref reader, version);
        Assert.Equal(hello.PluginVersion, decoded.PluginVersion);
        Assert.Equal(hello.GameBuild, decoded.GameBuild);
        Assert.Equal(hello.ModFingerprint, decoded.ModFingerprint);
        Assert.Equal(hello.PlayerName, decoded.PlayerName);
    }

    [Fact]
    public void Welcome_RoundTrips()
    {
        var players = new[] { new PlayerInfo(PlayerId.Host, "Host"), new PlayerInfo(new PlayerId(2), "Two") };
        var welcome = new WelcomeMessage(new PlayerId(2), "Session", 4, players);
        var decoded = Decode(Encode(welcome.WriteTo), WelcomeMessage.ReadBody);

        Assert.Equal(new PlayerId(2), decoded.YourId);
        Assert.Equal("Session", decoded.SessionName);
        Assert.Equal(4, decoded.MaxPlayers);
        Assert.Equal(players, decoded.Players);
    }

    [Fact]
    public void Welcome_WithDuplicateIds_IsRejected()
    {
        var players = new[] { new PlayerInfo(PlayerId.Host, "A"), new PlayerInfo(PlayerId.Host, "B") };
        var bytes = Encode(new WelcomeMessage(PlayerId.Host, "S", 4, players).WriteTo);
        Assert.Throws<ProtocolViolationException>(() => Decode(bytes, WelcomeMessage.ReadBody));
    }

    [Fact]
    public void Welcome_NotListingTheReceiver_IsRejected()
    {
        var players = new[] { new PlayerInfo(PlayerId.Host, "A") };
        var bytes = Encode(new WelcomeMessage(new PlayerId(3), "S", 4, players).WriteTo);
        Assert.Throws<ProtocolViolationException>(() => Decode(bytes, WelcomeMessage.ReadBody));
    }

    [Fact]
    public void Welcome_WithMorePlayersThanMax_IsRejected()
    {
        var players = new[] { new PlayerInfo(PlayerId.Host, "A"), new PlayerInfo(new PlayerId(2), "B"), new PlayerInfo(new PlayerId(3), "C") };
        var bytes = Encode(new WelcomeMessage(new PlayerId(2), "S", 2, players).WriteTo);
        Assert.Throws<ProtocolViolationException>(() => Decode(bytes, WelcomeMessage.ReadBody));
    }

    [Fact]
    public void AuthChallenge_RoundTrips_AndRejectsAbusiveIterationCounts()
    {
        var challenge = new AuthChallengeMessage(new byte[ProtocolConstants.AuthNonceSize], new byte[ProtocolConstants.AuthSaltSize], 1234);
        var decoded = Decode(Encode(challenge.WriteTo), AuthChallengeMessage.ReadBody);
        Assert.Equal(1234, decoded.Iterations);

        var abusive = new AuthChallengeMessage(new byte[ProtocolConstants.AuthNonceSize], new byte[ProtocolConstants.AuthSaltSize], ProtocolConstants.MaxPasswordIterations + 1);
        Assert.Throws<ProtocolViolationException>(() => Decode(Encode(abusive.WriteTo), AuthChallengeMessage.ReadBody));
    }

    [Fact]
    public void PlayerLeft_RejectsPlayerIdZero()
    {
        var bytes = Encode(new PlayerLeftMessage(PlayerId.None, DisconnectReason.ClientLeft).WriteTo);
        Assert.Throws<ProtocolViolationException>(() => Decode(bytes, PlayerLeftMessage.ReadBody));
    }

    [Fact]
    public void Chat_RoundTrips()
    {
        var decoded = Decode(Encode(new ChatMessage(new PlayerId(3), "o7 engineers").WriteTo), ChatMessage.ReadBody);
        Assert.Equal(new PlayerId(3), decoded.From);
        Assert.Equal("o7 engineers", decoded.Text);
    }

    [Fact]
    public void PasswordProof_VerifiesOnlyTheRightPassword()
    {
        var salt = PasswordProof.CreateRandom(ProtocolConstants.AuthSaltSize);
        var nonce = PasswordProof.CreateRandom(ProtocolConstants.AuthNonceSize);
        var hostKey = PasswordProof.DeriveKey("hunter2", salt, 1_000);

        var good = PasswordProof.ComputeMac(PasswordProof.DeriveKey("hunter2", salt, 1_000), nonce);
        var bad = PasswordProof.ComputeMac(PasswordProof.DeriveKey("hunter3", salt, 1_000), nonce);

        Assert.True(PasswordProof.Verify(hostKey, nonce, good));
        Assert.False(PasswordProof.Verify(hostKey, nonce, bad));
        Assert.False(PasswordProof.Verify(hostKey, PasswordProof.CreateRandom(ProtocolConstants.AuthNonceSize), good)); // replay against a new nonce
    }

    private static byte[] Encode(Action<PacketWriter> write)
    {
        using var writer = new PacketWriter();
        write(writer);
        return writer.WrittenSpan.ToArray();
    }

    private static T Decode<T>(byte[] bytes, BodyReader<T> read)
    {
        var reader = new PacketReader(bytes);
        reader.ReadByte();
        return read(ref reader);
    }
}
