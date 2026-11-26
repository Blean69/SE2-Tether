// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.Linq;
using Tether.Diagnostics;
using Tether.Protocol;
using Tether.Transport.Loopback;
using Tether.Transport.Udp;
using Xunit;

namespace Tether.Tests.Diagnostics;

public class NetTestTests
{
    [Theory]
    [InlineData("example.org", "example.org", 27027)]
    [InlineData("192.168.1.20:5000", "192.168.1.20", 5000)]
    [InlineData(" 10.0.0.1 ", "10.0.0.1", 27027)]
    [InlineData("[::1]:6000", "::1", 6000)]
    [InlineData("[fe80::1]", "fe80::1", 27027)]
    [InlineData("fe80::1", "fe80::1", 27027)]
    public void Endpoint_Parses(string text, string host, int port)
    {
        Assert.True(UdpEndpoint.TryParse(text, 27027, out var endpoint));
        Assert.Equal(new UdpEndpoint(host, port), endpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("host:")]
    [InlineData("host:0")]
    [InlineData("host:70000")]
    [InlineData("host:abc")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData(":5000")]
    public void Endpoint_RejectsInvalid(string text) =>
        Assert.False(UdpEndpoint.TryParse(text, 27027, out _));

    [Fact]
    public void LinkProbe_CountsGapsAsLoss_AndIgnoresForeignPayloads()
    {
        var clock = new ManualClock();
        var sender = new LinkProbe(clock, intervalMs: 10);
        var receiver = new LinkProbe(clock);
        var buffer = new byte[LinkProbe.PacketSize];
        var from = new PlayerId(2);

        for (var i = 0; i < 100; i++)
        {
            clock.Advance(10);
            Assert.True(sender.TryCreateProbe(buffer));
            Assert.False(sender.TryCreateProbe(buffer)); // not due again yet
            if (i % 10 != 3)
            {
                Assert.True(receiver.TryReceive(from, buffer));
            }
        }

        Assert.False(receiver.TryReceive(from, new byte[] { 1, 2, 3 }));
        var sample = receiver.TakeSamples().Single();
        Assert.Equal(from, sample.Player);
        Assert.Equal(90, sample.Received);
        Assert.InRange(sample.LossPercent, 9.0, 11.0); // the very first probe isn't a measurable gap
        Assert.Equal(0, receiver.TakeSamples().Single().Expected); // window resets
    }

    [Fact]
    public void Nodes_HostAndClient_GreetChatAndMeasureTheLink()
    {
        var clock = new ManualClock(1_000);
        var network = new LoopbackNetwork(clock, new LoopbackConditions { LatencyMs = 40, UnreliableLoss = 0.1 }, seed: 3);
        var hostLines = new List<string>();
        var clientLines = new List<string>();

        using var host = NetTestNode.StartHost(network.CreateHost(), new NetTestOptions { Name = "SE2 host", Greeting = "running inside a test", ReportIntervalMs = 0 }, clock);
        using var client = NetTestNode.StartClient(network.CreateClient(), new NetTestOptions { Name = "Laptop", Greeting = "running tether-nettest", ReportIntervalMs = 0 }, clock);
        host.Output += hostLines.Add;
        client.Output += clientLines.Add;

        for (var t = 0; t < 5_000; t += 10)
        {
            clock.Advance(10);
            host.Poll();
            client.Poll();
        }

        Assert.True(client.IsJoined);
        Assert.Contains(hostLines, l => l.Contains("Laptop (#2) joined"));
        Assert.Contains(clientLines, l => l.Contains("Welcome, Laptop! This host is running inside a test."));
        Assert.Contains(hostLines, l => l.Contains("<Laptop> Hello from Laptop, running tether-nettest."));

        var link = host.DescribeLink();
        Assert.Contains("Laptop: 80 ms RTT", link);
        Assert.Contains("% probe loss", link);
        Assert.Contains("SE2 host: 80 ms RTT", client.DescribeLink());
    }

    [Fact]
    public void ClientNode_Finishes_WhenTheHostGoesAway()
    {
        var clock = new ManualClock(1_000);
        var network = new LoopbackNetwork(clock);
        var host = NetTestNode.StartHost(network.CreateHost(), new NetTestOptions { Name = "Host" }, clock);
        using var client = NetTestNode.StartClient(network.CreateClient(), new NetTestOptions { Name = "Client" }, clock);
        for (var t = 0; t < 500 && !client.IsJoined; t += 10)
        {
            clock.Advance(10);
            host.Poll();
            client.Poll();
        }

        Assert.True(client.IsJoined);
        host.Dispose();
        for (var t = 0; t < 500 && !client.IsFinished; t += 10)
        {
            clock.Advance(10);
            client.Poll();
        }

        Assert.True(client.IsFinished);
        Assert.Equal(DisconnectReason.HostShutdown, client.EndReason);
    }
}
