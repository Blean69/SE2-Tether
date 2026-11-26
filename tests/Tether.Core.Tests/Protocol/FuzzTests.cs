// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using Tether.Protocol;
using Tether.Session;
using Tether.Tests.Support;
using Tether.Transport;
using Xunit;

namespace Tether.Tests.Protocol;

/// <summary>
/// Throws random and mutated bytes at every decoder and at a live host. The rule under test is that
/// hostile input only ever produces <see cref="ProtocolViolationException"/> or a disconnect of the
/// sender, never a crash, a hang, or an effect on other players.
/// </summary>
public class FuzzTests
{
    private const int Iterations = 20_000;

    private static readonly List<byte[]> Seeds = BuildSeeds();

    [Fact]
    public void Decoders_OnlyEverThrowProtocolViolation()
    {
        var random = new Random(1234);
        for (var i = 0; i < Iterations; i++)
        {
            var input = i % 2 == 0 ? RandomBytes(random) : Mutate(random, Seeds[random.Next(Seeds.Count)]);
            try
            {
                DecodeAny(input);
            }
            catch (ProtocolViolationException)
            {
            }
            catch (Exception ex)
            {
                Assert.Fail($"Input {Convert.ToHexString(input)} threw {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    [Fact]
    public void Host_SurvivesHostileClients_AndHonestPlayersStayConnected()
    {
        using var h = new LoopbackHarness(new HostOptions { MaxPlayers = 4, Password = "pw", PasswordIterations = 1_000 });
        var honest = h.AddClient("Honest", password: "pw");
        Assert.True(h.RunUntil(() => honest.State == ClientState.Joined));

        var random = new Random(99);
        var disconnected = 0;
        for (var round = 0; round < 200; round++)
        {
            var raw = h.AddRaw();
            Assert.True(h.RunUntil(() => raw.IsConnected));
            for (var i = 0; i < 20 && raw.IsConnected; i++)
            {
                var channel = (byte)random.Next(Channels.Count);
                var payload = random.Next(3) == 0 ? RandomBytes(random) : Mutate(random, Seeds[random.Next(Seeds.Count)]);
                raw.Send(channel, payload);
                h.PollAll();
            }

            h.Run(50);
            if (raw.DisconnectReason is not null)
            {
                disconnected++;
            }

            raw.Transport.Dispose();
        }

        h.Run(500);
        Assert.Equal(ClientState.Joined, honest.State);
        Assert.Empty(h.Log.Errors);
        Assert.True(disconnected > 150, $"Only {disconnected} of 200 hostile peers were disconnected.");
        Assert.Equal(2, h.Host.Players.Count);
    }

    private static void DecodeAny(byte[] input)
    {
        var reader = new PacketReader(input);
        var type = (MessageType)reader.ReadByte();
        switch (type)
        {
            case MessageType.Hello:
                var version = HelloMessage.ReadProtocolVersion(ref reader);
                HelloMessage.ReadRemainder(ref reader, version);
                break;
            case MessageType.AuthChallenge:
                AuthChallengeMessage.ReadBody(ref reader);
                break;
            case MessageType.AuthResponse:
                AuthResponseMessage.ReadBody(ref reader);
                break;
            case MessageType.Welcome:
                WelcomeMessage.ReadBody(ref reader);
                break;
            case MessageType.PlayerJoined:
                PlayerJoinedMessage.ReadBody(ref reader);
                break;
            case MessageType.PlayerLeft:
                PlayerLeftMessage.ReadBody(ref reader);
                break;
            case MessageType.ChatSend:
                ChatSendMessage.ReadBody(ref reader);
                break;
            case MessageType.ChatMessage:
                ChatMessage.ReadBody(ref reader);
                break;
        }
    }

    private static List<byte[]> BuildSeeds()
    {
        var seeds = new List<byte[]>();
        void Add(Action<PacketWriter> write)
        {
            using var writer = new PacketWriter();
            write(writer);
            seeds.Add(writer.WrittenSpan.ToArray());
        }

        var players = new[] { new PlayerInfo(PlayerId.Host, "Host"), new PlayerInfo(new PlayerId(2), "Two") };
        Add(w => new HelloMessage(ProtocolConstants.ProtocolVersion, "0.1.0", "build", new byte[32], "Name", PeerCapabilities.None).WriteTo(w));
        Add(w => new AuthChallengeMessage(new byte[ProtocolConstants.AuthNonceSize], new byte[ProtocolConstants.AuthSaltSize], 1000).WriteTo(w));
        Add(w => new AuthResponseMessage(new byte[ProtocolConstants.AuthMacSize]).WriteTo(w));
        Add(w => new WelcomeMessage(new PlayerId(2), "Session", 4, players).WriteTo(w));
        Add(w => new PlayerJoinedMessage(players[1]).WriteTo(w));
        Add(w => new PlayerLeftMessage(new PlayerId(2), DisconnectReason.ClientLeft).WriteTo(w));
        Add(w => new ChatSendMessage("hello").WriteTo(w));
        Add(w => new ChatMessage(new PlayerId(2), "hello").WriteTo(w));
        Add(w => EngineDataMessage.WriteTo(w, new byte[] { 1, 2, 3 }));
        return seeds;
    }

    private static byte[] RandomBytes(Random random)
    {
        var bytes = new byte[random.Next(0, 64)];
        random.NextBytes(bytes);
        return bytes;
    }

    private static byte[] Mutate(Random random, byte[] seed)
    {
        var bytes = new List<byte>(seed);
        var mutations = random.Next(1, 4);
        for (var m = 0; m < mutations; m++)
        {
            switch (random.Next(4))
            {
                case 0 when bytes.Count > 0:
                    bytes[random.Next(bytes.Count)] = (byte)random.Next(256);
                    break;
                case 1 when bytes.Count > 0:
                    bytes.RemoveAt(random.Next(bytes.Count));
                    break;
                case 2:
                    bytes.Insert(random.Next(bytes.Count + 1), (byte)random.Next(256));
                    break;
                default:
                    if (bytes.Count > 1)
                    {
                        var keep = random.Next(1, bytes.Count); // truncate
                        bytes.RemoveRange(keep, bytes.Count - keep);
                    }

                    break;
            }
        }

        return bytes.ToArray();
    }
}
