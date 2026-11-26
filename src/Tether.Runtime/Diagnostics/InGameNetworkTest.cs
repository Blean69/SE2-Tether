// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Threading;
using Tether.Diagnostics;
using Tether.Runtime.Bootstrap;
using Tether.Transport.Udp;

namespace Tether.Runtime.Diagnostics;

/// <summary>
/// Runs a Tether network test session from inside the game process, so we can prove that Tether's
/// networking works where it will really live: in the game on Windows, under Proton, and on native Linux.
/// </summary>
/// <remarks>
/// <para>Enable it with launch options, for example:</para>
/// <list type="bullet">
/// <item><c>-tether:host-test</c> or <c>-tether:host-test=27027</c> hosts a test session that tether-nettest
/// can join.</item>
/// <item><c>-tether:join-test=192.168.1.20</c> or <c>-tether:join-test=example.org:27027</c> joins a
/// tether-nettest host.</item>
/// <item>Optional: <c>-tether:name=YourName</c>, <c>-tether:password=secret</c>.</item>
/// </list>
/// <para>
/// It doesn't touch the game. It runs on its own background thread, because Tether doesn't hook the game
/// loop yet (that's Phase 1). Everything it sees goes to tether.log.
/// </para>
/// </remarks>
internal sealed class InGameNetworkTest : IDisposable
{
    private readonly NetTestNode _node;
    private readonly ITetherLog _log;
    private readonly Thread _thread;
    private volatile bool _stopping;

    private InGameNetworkTest(NetTestNode node, ITetherLog log)
    {
        _node = node;
        _log = log;
        _thread = new Thread(Run) { IsBackground = true, Name = "Tether network self-test" };
    }

    public static InGameNetworkTest? StartIfRequested(LaunchOptions options, ITetherLog log, string platform)
    {
        var hostRequested = options.Values.TryGetValue("host-test", out var hostValue);
        var joinRequested = options.Values.TryGetValue("join-test", out var joinValue);
        if (!hostRequested && !joinRequested)
        {
            return null;
        }

        if (hostRequested && joinRequested)
        {
            log.Warn("Network self-test: use either -tether:host-test or -tether:join-test, not both. Not starting.");
            return null;
        }

        var testOptions = new NetTestOptions
        {
            Name = options.Values.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name) ? name! : hostRequested ? "SE2 host" : "SE2 client",
            Password = options.Values.TryGetValue("password", out var password) ? password : null,
            Greeting = $"running inside Space Engineers 2 ({platform})",
        };

        NetTestNode node;
        if (hostRequested)
        {
            var port = UdpTransportOptions.DefaultPort;
            if (!string.IsNullOrWhiteSpace(hostValue) && (!int.TryParse(hostValue, out port) || port is < 1 or > 65535))
            {
                log.Warn($"Network self-test: '{hostValue}' is not a valid port. Not starting.");
                return null;
            }

            var transport = UdpTransport.CreateHost(port, log: log);
            node = NetTestNode.StartHost(transport, testOptions, log: log);
            log.Info($"Network self-test: hosting on UDP port {transport.LocalPort}. Join with: tether-nettest join <this PC's address>:{transport.LocalPort}");
        }
        else
        {
            if (!UdpEndpoint.TryParse(joinValue, UdpTransportOptions.DefaultPort, out var target))
            {
                log.Warn($"Network self-test: '{joinValue}' is not a valid host[:port]. Not starting.");
                return null;
            }

            node = NetTestNode.StartClient(UdpTransport.CreateClient(target.Host, target.Port, log: log), testOptions, log: log);
            log.Info($"Network self-test: joining {target}.");
        }

        var test = new InGameNetworkTest(node, log);
        node.Output += line => log.Info("Network self-test: " + line);
        test._thread.Start();
        return test;
    }

    public void Dispose()
    {
        _stopping = true;
        if (!_thread.Join(TimeSpan.FromSeconds(2)))
        {
            _log.Warn("Network self-test thread did not stop in time.");
        }
    }

    private void Run()
    {
        try
        {
            while (!_stopping && !_node.IsFinished)
            {
                _node.Poll();
                Thread.Sleep(15);
            }

            if (_node.IsFinished && !_stopping)
            {
                _log.Info("Network self-test: session ended. Restart the game to test again.");
            }
        }
        catch (Exception ex)
        {
            _log.Error("Network self-test crashed and stopped. The game is unaffected.", ex);
        }
        finally
        {
            try
            {
                _node.Dispose();
            }
            catch (Exception ex)
            {
                _log.Warn("Network self-test cleanup failed.", ex);
            }
        }
    }
}
