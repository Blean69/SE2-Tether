// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Tether.Protocol;
using Tether.Session;
using Tether.Tests.Support;
using Tether.Transport;
using Tether.Transport.Udp;
using Xunit;

namespace Tether.Tests.Transport;

/// <summary>End-to-end over real UDP sockets on localhost, using LiteNetLib.</summary>
public class UdpTransportTests
{
    private static readonly UdpTransportOptions FastTimeouts = new() { DisconnectTimeoutMs = 3_000, EnableIPv6 = false };

    [Fact]
    public void TwoClients_Join_Chat_AndExchangeEngineData_OverRealSockets()
    {
        var log = new TestLog();
        var hostTransport = UdpTransport.CreateHost(0, FastTimeouts, log);
        using var host = new HostSession(hostTransport, new HostOptions { HostPlayerName = "Host", Password = "pw", PasswordIterations = 1_000 }, log: log);
        host.Start();
        var port = hostTransport.LocalPort;
        Assert.True(port > 0);

        using var a = NewClient("Alice", port, log);
        using var b = NewClient("Bob", port, log);
        var sessions = new Action[] { host.Poll, a.Poll, b.Poll };
        Assert.True(PumpUntil(sessions, () => a.State == ClientState.Joined && b.State == ClientState.Joined), Dump(log));

        var bChat = new List<string>();
        var hostData = new List<byte[]>();
        var bigReceived = new List<int>();
        b.ChatReceived += (p, t) => bChat.Add($"{p.Name}: {t}");
        host.EngineDataReceived += (_, _, d) => hostData.Add(d.ToArray());
        b.EngineDataReceived += (_, ch, d) => { if (ch == Channels.Bulk) { bigReceived.Add(d.Length); } };

        a.SendChat("hello over udp");
        a.SendEngineData(Channels.EngineUnreliable, new byte[] { 42 });
        var big = new byte[200_000]; // fragmented by LiteNetLib
        Assert.Equal(SendResult.Sent, host.SendEngineData(b.LocalPlayerId, Channels.Bulk, big));

        Assert.True(PumpUntil(sessions, () => bChat.Count == 1 && hostData.Count == 1 && bigReceived.Count == 1), Dump(log));
        Assert.Equal("Alice: hello over udp", bChat[0]);
        Assert.Equal(new byte[] { 42 }, hostData[0]);
        Assert.Equal(200_000, bigReceived[0]);
        Assert.Empty(log.Errors);
    }

    [Fact]
    public void Kick_ReasonAndMessage_ReachTheClient()
    {
        var log = new TestLog();
        var hostTransport = UdpTransport.CreateHost(0, FastTimeouts, log);
        using var host = new HostSession(hostTransport, new HostOptions(), log: log);
        host.Start();
        using var a = NewClient("Alice", hostTransport.LocalPort, log);
        var sessions = new Action[] { host.Poll, a.Poll };
        Assert.True(PumpUntil(sessions, () => a.State == ClientState.Joined), Dump(log));

        host.Kick(a.LocalPlayerId, "AFK too long");
        Assert.True(PumpUntil(sessions, () => a.State == ClientState.Disconnected), Dump(log));
        Assert.Equal(DisconnectReason.Kicked, a.EndReason);
        Assert.Equal("AFK too long", a.EndMessage);
    }

    [Fact]
    public void Join_WorksUnderSimulatedLatencyAndLoss()
    {
        var lossy = FastTimeouts with { SimulatedLatencyMs = (30, 60), SimulatedPacketLossPercent = 10 };
        var log = new TestLog();
        var hostTransport = UdpTransport.CreateHost(0, lossy, log);
        using var host = new HostSession(hostTransport, new HostOptions(), log: log);
        host.Start();
        using var a = new ClientSession(UdpTransport.CreateClient("127.0.0.1", hostTransport.LocalPort, lossy, log), new ClientOptions { PlayerName = "Alice" }, log: log);
        a.Start();
        var sessions = new Action[] { host.Poll, a.Poll };
        Assert.True(PumpUntil(sessions, () => a.State == ClientState.Joined, TimeSpan.FromSeconds(15)), Dump(log));

        var received = new List<int>();
        host.EngineDataReceived += (_, _, d) => received.Add(BitConverter.ToInt32(d));
        for (var i = 0; i < 200; i++)
        {
            a.SendEngineData(Channels.EngineReliable, BitConverter.GetBytes(i));
        }

        Assert.True(PumpUntil(sessions, () => received.Count == 200, TimeSpan.FromSeconds(15)), Dump(log));
        Assert.Equal(Enumerable.Range(0, 200), received);
    }

    [Fact]
    public void Connecting_ToAPortNobodyListensOn_Fails()
    {
        var log = new TestLog();
        // Bind and release a port so we know nothing listens there.
        var probe = UdpTransport.CreateHost(0, FastTimeouts, log);
        probe.Start(new NullEvents());
        var port = probe.LocalPort;
        probe.Dispose();

        var options = FastTimeouts;
        using var a = new ClientSession(UdpTransport.CreateClient("127.0.0.1", port, options, log), new ClientOptions { PlayerName = "Alice", JoinTimeoutMs = 20_000 }, log: log);
        a.Start();
        Assert.True(PumpUntil(new Action[] { a.Poll }, () => a.State == ClientState.Disconnected, TimeSpan.FromSeconds(15)), Dump(log));
        Assert.Equal(DisconnectReason.ConnectionFailed, a.EndReason);
    }

    [Fact]
    public void DisconnectPayload_RoundTrips()
    {
        var encoded = UdpTransport.EncodeDisconnect(DisconnectReason.Kicked, "bye\u0000 now");
        var (reason, message) = UdpTransport.DecodeDisconnect(encoded, DisconnectReason.Unknown);
        Assert.Equal(DisconnectReason.Kicked, reason);
        Assert.Equal("bye now", message);

        Assert.Equal((DisconnectReason.Unknown, (string?)null), UdpTransport.DecodeDisconnect(new byte[] { 250 }, DisconnectReason.Timeout));
        Assert.Equal((DisconnectReason.Timeout, (string?)null), UdpTransport.DecodeDisconnect(ReadOnlySpan<byte>.Empty, DisconnectReason.Timeout));
    }

    private static ClientSession NewClient(string name, int port, TestLog log)
    {
        var client = new ClientSession(
            UdpTransport.CreateClient("127.0.0.1", port, FastTimeouts, log),
            new ClientOptions { PlayerName = name, Password = "pw" },
            log: log);
        client.Start();
        return client;
    }

    private static bool PumpUntil(Action[] polls, Func<bool> condition, TimeSpan? timeout = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        while (stopwatch.Elapsed < limit)
        {
            foreach (var poll in polls)
            {
                poll();
            }

            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return condition();
    }

    private static string Dump(TestLog log)
    {
        lock (log.Lines)
        {
            return string.Join(Environment.NewLine, log.Lines);
        }
    }

    private sealed class NullEvents : ITransportEvents
    {
        public void OnConnected(TransportConnection connection)
        {
        }

        public void OnDisconnected(TransportConnection connection, DisconnectReason reason, string? message)
        {
        }

        public void OnReceived(TransportConnection connection, byte channel, ReadOnlySpan<byte> payload)
        {
        }
    }
}
