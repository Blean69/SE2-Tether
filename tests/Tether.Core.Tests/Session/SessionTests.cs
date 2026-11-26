// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.Linq;
using Tether.Protocol;
using Tether.Session;
using Tether.Tests.Support;
using Tether.Transport;
using Tether.Transport.Loopback;
using Xunit;

namespace Tether.Tests.Session;

public class SessionTests
{
    [Fact]
    public void Client_Joins_AndEveryoneAgreesOnThePlayerList()
    {
        using var h = new LoopbackHarness();
        var hostJoins = new List<PlayerInfo>();
        h.Host.PlayerJoined += hostJoins.Add;

        var a = h.AddClient("Alice");
        Assert.True(h.RunUntil(() => a.State == ClientState.Joined));

        var aSawJoin = new List<PlayerInfo>();
        a.PlayerJoined += aSawJoin.Add;
        var b = h.AddClient("Bob");
        Assert.True(h.RunUntil(() => b.State == ClientState.Joined && aSawJoin.Count == 1));

        Assert.Equal("Test session", b.SessionName);
        Assert.Equal(SessionDefaults.MaxPlayers, b.MaxPlayers);
        Assert.Equal(new PlayerId(2), a.LocalPlayerId);
        Assert.Equal(new PlayerId(3), b.LocalPlayerId);
        Assert.Equal(new[] { "Hosty", "Alice", "Bob" }, h.Host.Players.Select(p => p.Name));
        Assert.Equal(h.Host.Players, b.Players);
        Assert.Equal(h.Host.Players, a.Players);
        Assert.Equal(new[] { "Alice", "Bob" }, hostJoins.Select(p => p.Name));
        Assert.Equal("Bob", aSawJoin.Single().Name);
        Assert.Empty(h.Log.Errors);
    }

    [Fact]
    public void FifthPlayer_IsRefused_SessionFull()
    {
        using var h = new LoopbackHarness();
        var clients = Enumerable.Range(1, 3).Select(i => h.AddClient($"P{i}")).ToList();
        Assert.True(h.RunUntil(() => clients.All(c => c.State == ClientState.Joined)));

        var extra = h.AddClient("TooMany");
        Assert.True(h.RunUntil(() => extra.State == ClientState.Disconnected));
        Assert.Equal(DisconnectReason.SessionFull, extra.EndReason);
        Assert.Equal(4, h.Host.Players.Count);
    }

    [Fact]
    public void SlotFreedByLeaving_CanBeReused_AndIdIsRecycled()
    {
        using var h = new LoopbackHarness(new HostOptions { MaxPlayers = 2 });
        var first = h.AddClient("First");
        Assert.True(h.RunUntil(() => first.State == ClientState.Joined));

        var left = new List<(PlayerInfo, DisconnectReason)>();
        h.Host.PlayerLeft += (p, r) => left.Add((p, r));
        first.Leave();
        Assert.True(h.RunUntil(() => left.Count == 1));
        Assert.Equal(DisconnectReason.ClientLeft, left[0].Item2);

        var second = h.AddClient("Second");
        Assert.True(h.RunUntil(() => second.State == ClientState.Joined));
        Assert.Equal(new PlayerId(2), second.LocalPlayerId);
    }

    [Fact]
    public void Password_Correct_Joins()
    {
        using var h = new LoopbackHarness(new HostOptions { Password = "hunter2", PasswordIterations = 1_000 });
        var client = h.AddClient("Alice", password: "hunter2");
        Assert.True(h.RunUntil(() => client.State == ClientState.Joined));
    }

    [Fact]
    public void Password_Wrong_IsRefused()
    {
        using var h = new LoopbackHarness(new HostOptions { Password = "hunter2", PasswordIterations = 1_000 });
        var client = h.AddClient("Mallory", password: "letmein");
        Assert.True(h.RunUntil(() => client.State == ClientState.Disconnected));
        Assert.Equal(DisconnectReason.WrongPassword, client.EndReason);
        Assert.Single(h.Host.Players);
    }

    [Fact]
    public void Password_Missing_ReportsPasswordRequired()
    {
        using var h = new LoopbackHarness(new HostOptions { Password = "hunter2", PasswordIterations = 1_000 });
        var client = h.AddClient("Alice");
        Assert.True(h.RunUntil(() => client.State == ClientState.Disconnected));
        Assert.Equal(DisconnectReason.PasswordRequired, client.EndReason);
    }

    [Fact]
    public void GameBuildMismatch_IsRefused_WithTheHostBuildInTheMessage()
    {
        using var h = new LoopbackHarness(new HostOptions { Game = new GameIdentity("4.0.0.3169", Array.Empty<byte>()) });
        var client = h.AddClient("Old", game: new GameIdentity("4.0.0.3155", Array.Empty<byte>()));
        Assert.True(h.RunUntil(() => client.State == ClientState.Disconnected));
        Assert.Equal(DisconnectReason.GameBuildMismatch, client.EndReason);
        Assert.Contains("4.0.0.3169", client.EndMessage);
    }

    [Fact]
    public void ModMismatch_IsRefused()
    {
        var hostMods = GameIdentity.ComputeModFingerprint(new[] { "workshop:1", "workshop:2" });
        var clientMods = GameIdentity.ComputeModFingerprint(new[] { "workshop:1" });
        using var h = new LoopbackHarness(new HostOptions { Game = new GameIdentity("b", hostMods) });
        var client = h.AddClient("Modless", game: new GameIdentity("b", clientMods));
        Assert.True(h.RunUntil(() => client.State == ClientState.Disconnected));
        Assert.Equal(DisconnectReason.ModMismatch, client.EndReason);
    }

    [Fact]
    public void ModFingerprint_IgnoresOrderAndDuplicates() =>
        Assert.Equal(
            GameIdentity.ComputeModFingerprint(new[] { "b", "a", "a" }),
            GameIdentity.ComputeModFingerprint(new[] { "a", "b" }));

    [Fact]
    public void FutureProtocolVersion_IsRefusedCleanly()
    {
        using var h = new LoopbackHarness();
        var raw = h.AddRaw();
        Assert.True(h.RunUntil(() => raw.IsConnected));

        // A future client: correct frozen prefix, then a body this version can't parse.
        raw.Send(Channels.Control, w =>
        {
            w.WriteByte((byte)MessageType.Hello);
            w.WriteUInt16(ProtocolConstants.ProtocolVersion + 1);
            w.WriteRaw(new byte[] { 0xFF, 0xFF, 0xFF });
        });

        Assert.True(h.RunUntil(() => raw.DisconnectReason is not null));
        Assert.Equal(DisconnectReason.ProtocolMismatch, raw.DisconnectReason);
        Assert.Contains($"protocol {ProtocolConstants.ProtocolVersion}", raw.DisconnectMessage);
    }

    [Fact]
    public void SilentConnection_IsDroppedAfterTheHandshakeTimeout()
    {
        using var h = new LoopbackHarness(new HostOptions { HandshakeTimeoutMs = 2_000 });
        var raw = h.AddRaw();
        Assert.True(h.RunUntil(() => raw.IsConnected));

        h.Run(1_500);
        Assert.Null(raw.DisconnectReason);
        Assert.True(h.RunUntil(() => raw.DisconnectReason is not null, 2_000));
        Assert.Equal(DisconnectReason.HandshakeTimeout, raw.DisconnectReason);
    }

    [Fact]
    public void Client_GivesUp_WhenNoHostAnswers()
    {
        using var h = new LoopbackHarness();
        var lonely = new ClientSession(
            h.Network.CreateClient("nobody-home"),
            new ClientOptions { PlayerName = "Lonely" },
            h.Clock);
        DisconnectReason? reason = null;
        lonely.Disconnected += (r, _) => reason = r;
        lonely.Start();

        Assert.True(h.RunUntil(() => { lonely.Poll(); return reason is not null; }));
        Assert.Equal(DisconnectReason.ConnectionFailed, reason);
    }

    [Fact]
    public void Chat_IsRelayedToEveryone_AttributedByTheHost()
    {
        using var h = new LoopbackHarness();
        var a = h.AddClient("Alice");
        var b = h.AddClient("Bob");
        Assert.True(h.RunUntil(() => a.State == ClientState.Joined && b.State == ClientState.Joined));

        var hostSaw = new List<(string, string)>();
        var bSaw = new List<(string, string)>();
        var aSaw = new List<(string, string)>();
        h.Host.ChatReceived += (p, t) => hostSaw.Add((p.Name, t));
        a.ChatReceived += (p, t) => aSaw.Add((p.Name, t));
        b.ChatReceived += (p, t) => bSaw.Add((p.Name, t));

        a.SendChat("  hello‮ world  ");
        h.Host.SendChat("welcome aboard");
        Assert.True(h.RunUntil(() => bSaw.Count == 2 && aSaw.Count == 2));

        Assert.Contains(("Alice", "hello world"), hostSaw);
        Assert.Contains(("Alice", "hello world"), bSaw);
        Assert.Contains(("Alice", "hello world"), aSaw); // echo back to the sender
        Assert.Contains(("Hosty", "welcome aboard"), bSaw);
    }

    [Fact]
    public void ChatFlood_IsThrottled_NotPunished()
    {
        using var h = new LoopbackHarness();
        var a = h.AddClient("Spammer");
        Assert.True(h.RunUntil(() => a.State == ClientState.Joined));
        var hostSaw = 0;
        h.Host.ChatReceived += (_, _) => hostSaw++;

        for (var i = 0; i < 50; i++)
        {
            a.SendChat($"spam {i}");
        }

        h.Run(500);
        Assert.Equal(RateLimits.Default.ChatBurst, hostSaw);
        Assert.Equal(ClientState.Joined, a.State);
    }

    [Fact]
    public void EngineData_FlowsBothWays_OnTheRequestedChannel()
    {
        using var h = new LoopbackHarness();
        var a = h.AddClient("Alice");
        var b = h.AddClient("Bob");
        Assert.True(h.RunUntil(() => a.State == ClientState.Joined && b.State == ClientState.Joined));

        var hostGot = new List<(PlayerId, byte, byte[])>();
        var aGot = new List<(byte, byte[])>();
        var bGot = new List<(byte, byte[])>();
        h.Host.EngineDataReceived += (from, ch, data) => hostGot.Add((from, ch, data.ToArray()));
        a.EngineDataReceived += (_, ch, data) => aGot.Add((ch, data.ToArray()));
        b.EngineDataReceived += (_, ch, data) => bGot.Add((ch, data.ToArray()));

        Assert.Equal(SendResult.Sent, a.SendEngineData(Channels.EngineUnreliable, new byte[] { 1, 2, 3 }));
        Assert.Equal(SendResult.Sent, h.Host.SendEngineData(b.LocalPlayerId, Channels.EngineReliable, new byte[] { 4 }));
        h.Host.BroadcastEngineData(Channels.Bulk, new byte[] { 5, 5 }, except: a.LocalPlayerId);

        Assert.True(h.RunUntil(() => hostGot.Count == 1 && bGot.Count == 2));
        Assert.Equal((a.LocalPlayerId, Channels.EngineUnreliable), (hostGot[0].Item1, hostGot[0].Item2));
        Assert.Equal(new byte[] { 1, 2, 3 }, hostGot[0].Item3);
        Assert.Contains(bGot, m => m.Item1 == Channels.EngineReliable && m.Item2.SequenceEqual(new byte[] { 4 }));
        Assert.Contains(bGot, m => m.Item1 == Channels.Bulk && m.Item2.SequenceEqual(new byte[] { 5, 5 }));
        Assert.Empty(aGot);
    }

    [Fact]
    public void EngineData_OnTheControlChannel_IsRefusedByTheApi() =>
        Assert.Equal(SendResult.InvalidChannel, new ClientSession(new LoopbackNetwork(new Tether.Diagnostics.ManualClock()).CreateClient(), new ClientOptions()).SendEngineData(Channels.Control, new byte[] { 1 }));

    [Fact]
    public void EngineData_BeforeJoining_IsAProtocolViolation()
    {
        using var h = new LoopbackHarness();
        var raw = h.AddRaw();
        Assert.True(h.RunUntil(() => raw.IsConnected));
        var hostGot = 0;
        h.Host.EngineDataReceived += (_, _, _) => hostGot++;

        raw.Send(Channels.EngineReliable, w => EngineDataMessage.WriteTo(w, new byte[] { 1 }));
        Assert.True(h.RunUntil(() => raw.DisconnectReason is not null));
        Assert.Equal(DisconnectReason.ProtocolViolation, raw.DisconnectReason);
        Assert.Equal(0, hostGot);
    }

    [Fact]
    public void TrafficFlood_DisconnectsOnlyTheFlooder()
    {
        using var h = new LoopbackHarness(new HostOptions { ClientLimits = new RateLimits { MessagesPerSecond = 50, MessageBurst = 100 } });
        var flooder = h.AddClient("Flooder");
        var bystander = h.AddClient("Bystander");
        Assert.True(h.RunUntil(() => flooder.State == ClientState.Joined && bystander.State == ClientState.Joined));

        for (var i = 0; i < 500; i++)
        {
            flooder.SendEngineData(Channels.EngineReliable, new byte[] { 1 });
        }

        Assert.True(h.RunUntil(() => flooder.State == ClientState.Disconnected));
        Assert.Equal(DisconnectReason.RateLimited, flooder.EndReason);
        h.Run(200);
        Assert.Equal(ClientState.Joined, bystander.State);
        Assert.DoesNotContain(bystander.Players, p => p.Name == "Flooder");
    }

    [Fact]
    public void Kick_RemovesThePlayer_WithReasonAndMessage()
    {
        using var h = new LoopbackHarness();
        var a = h.AddClient("Alice");
        var b = h.AddClient("Bob");
        Assert.True(h.RunUntil(() => a.State == ClientState.Joined && b.State == ClientState.Joined));
        var bSawLeave = new List<(PlayerInfo, DisconnectReason)>();
        b.PlayerLeft += (p, r) => bSawLeave.Add((p, r));

        Assert.True(h.Host.Kick(a.LocalPlayerId, "griefing the refinery"));
        Assert.True(h.RunUntil(() => a.State == ClientState.Disconnected && bSawLeave.Count == 1));

        Assert.Equal(DisconnectReason.Kicked, a.EndReason);
        Assert.Equal("griefing the refinery", a.EndMessage);
        Assert.Equal(("Alice", DisconnectReason.Kicked), (bSawLeave[0].Item1.Name, bSawLeave[0].Item2));
        Assert.False(h.Host.Kick(a.LocalPlayerId));
    }

    [Fact]
    public void HostStop_DisconnectsEveryone_WithHostShutdown()
    {
        using var h = new LoopbackHarness();
        var a = h.AddClient("Alice");
        Assert.True(h.RunUntil(() => a.State == ClientState.Joined));

        h.Host.Stop();
        Assert.True(h.RunUntil(() => a.State == ClientState.Disconnected));
        Assert.Equal(DisconnectReason.HostShutdown, a.EndReason);
        Assert.False(h.Host.IsRunning);
    }

    [Fact]
    public void StoppingFromInsideAnEventHandler_IsSafe()
    {
        using var h = new LoopbackHarness();
        h.Host.PlayerJoined += _ => h.Host.Stop();
        var a = h.AddClient("Alice");
        Assert.True(h.RunUntil(() => a.State == ClientState.Disconnected));
        Assert.Equal(DisconnectReason.HostShutdown, a.EndReason);
        Assert.Empty(h.Log.Errors);
    }

    [Fact]
    public void DuplicateNames_GetASuffix()
    {
        using var h = new LoopbackHarness();
        var a = h.AddClient("Hosty");
        var b = h.AddClient("hosty");
        Assert.True(h.RunUntil(() => a.State == ClientState.Joined && b.State == ClientState.Joined));
        Assert.Equal(new[] { "Hosty", "Hosty (2)", "hosty (3)" }, h.Host.Players.Select(p => p.Name));
    }

    [Fact]
    public void BuggyEventHandler_DoesNotKickThePlayer()
    {
        using var h = new LoopbackHarness();
        h.Host.EngineDataReceived += (_, _, _) => throw new InvalidOperationException("adapter bug");
        var a = h.AddClient("Alice");
        Assert.True(h.RunUntil(() => a.State == ClientState.Joined));

        a.SendEngineData(Channels.EngineReliable, new byte[] { 1 });
        h.Run(200);
        Assert.Equal(ClientState.Joined, a.State);
        Assert.Single(h.Log.Errors);
    }

    [Fact]
    public void UnderLagJitterAndLoss_ReliableArrivesInOrder_UnreliableNeverGoesBackwards()
    {
        var conditions = new LoopbackConditions { LatencyMs = 200, JitterMs = 60, UnreliableLoss = 0.05, UnreliableReorder = 0.05 };
        using var h = new LoopbackHarness(conditions: conditions, seed: 42);
        var a = h.AddClient("Alice");
        Assert.True(h.RunUntil(() => a.State == ClientState.Joined, 10_000));

        var reliable = new List<int>();
        var unreliable = new List<int>();
        h.Host.EngineDataReceived += (_, ch, data) =>
            (ch == Channels.EngineReliable ? reliable : unreliable).Add(BitConverter.ToInt32(data));

        for (var i = 0; i < 1_000; i++)
        {
            a.SendEngineData(Channels.EngineReliable, BitConverter.GetBytes(i));
            a.SendEngineData(Channels.EngineUnreliable, BitConverter.GetBytes(i));
            h.Run(LoopbackHarness.StepMs);
        }

        h.Run(1_000);
        Assert.Equal(Enumerable.Range(0, 1_000), reliable);
        Assert.InRange(unreliable.Count, 850, 990); // ~5% lost, plus some reordered ones dropped as stale
        Assert.Equal(unreliable.OrderBy(x => x), unreliable);
        Assert.Equal(unreliable.Distinct().Count(), unreliable.Count);
    }
}
