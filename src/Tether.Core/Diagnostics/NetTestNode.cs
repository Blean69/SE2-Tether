// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.Linq;
using Tether.Protocol;
using Tether.Session;
using Tether.Transport;

namespace Tether.Diagnostics;

public sealed record NetTestOptions
{
    public string Name { get; init; } = "Tester";

    public string? Password { get; init; }

    public int MaxPlayers { get; init; } = SessionDefaults.MaxPlayers;

    /// <summary>
    /// Describes where this node runs, e.g. "inside Space Engineers 2 on Proton". The host includes it in
    /// its welcome message, and a client says it on joining.
    /// </summary>
    public string? Greeting { get; init; }

    /// <summary>How often a link-quality report is printed. 0 turns reports off.</summary>
    public int ReportIntervalMs { get; init; } = 10_000;

    public int ProbeIntervalMs { get; init; } = 100;

    public GameIdentity Game { get; init; } = GameIdentity.Unknown;
}

/// <summary>
/// One end of a Tether network test: a host or client session plus link probes and readable output.
/// Shared by the in-game self-test (-tether:host-test / -tether:join-test) and the tether-nettest tool.
/// </summary>
/// <remarks>Drive it from one thread by calling <see cref="Poll"/> frequently. Every 10–20 ms is ideal.</remarks>
public sealed class NetTestNode : IDisposable
{
    private readonly NetTestOptions _options;
    private readonly IClock _clock;
    private readonly HostSession? _host;
    private readonly ClientSession? _client;
    private readonly LinkProbe _probe;
    private readonly byte[] _probeBuffer = new byte[LinkProbe.PacketSize];
    private long _nextReportAt;
    private bool _finished;

    private NetTestNode(NetTestOptions options, IClock clock, HostSession? host, ClientSession? client)
    {
        _options = options;
        _clock = clock;
        _host = host;
        _client = client;
        _probe = new LinkProbe(clock, options.ProbeIntervalMs);
        _nextReportAt = clock.NowMs + options.ReportIntervalMs;
    }

    /// <summary>A human-readable line worth showing to the tester.</summary>
    public event Action<string>? Output;

    /// <summary>A chat line arrived (including our own, echoed by the host).</summary>
    public event Action<PlayerInfo, string>? ChatReceived;

    public bool IsHost => _host is not null;

    /// <summary>True once a client was disconnected, or a host was stopped.</summary>
    public bool IsFinished => _finished;

    public DisconnectReason? EndReason => _client?.EndReason;

    public bool IsJoined => _host?.IsRunning ?? _client!.State == ClientState.Joined;

    /// <summary>Our own id: always <see cref="PlayerId.Host"/> for a host; assigned on joining for a client.</summary>
    public PlayerId LocalPlayerId => _host is not null ? PlayerId.Host : _client!.LocalPlayerId;

    public IReadOnlyList<PlayerInfo> Players => _host is not null ? _host.Players : _client!.Players.ToList();

    public static NetTestNode StartHost(ITransport transport, NetTestOptions options, IClock? clock = null, ITetherLog? log = null)
    {
        clock ??= SystemClock.Instance;
        var host = new HostSession(transport, new HostOptions
        {
            SessionName = $"Tether network test ({options.Name})",
            HostPlayerName = options.Name,
            MaxPlayers = options.MaxPlayers,
            Password = options.Password,
            Game = options.Game,
        }, clock, log);
        var node = new NetTestNode(options, clock, host, null);
        node.WireHost(host);
        host.Start();
        node.Emit($"Hosting \"{host.SessionName}\" for up to {host.MaxPlayers} players{(host.RequiresPassword ? " (password protected)" : string.Empty)}.");
        return node;
    }

    public static NetTestNode StartClient(ITransport transport, NetTestOptions options, IClock? clock = null, ITetherLog? log = null)
    {
        clock ??= SystemClock.Instance;
        var client = new ClientSession(transport, new ClientOptions
        {
            PlayerName = options.Name,
            Password = options.Password,
            Game = options.Game,
        }, clock, log);
        var node = new NetTestNode(options, clock, null, client);
        node.WireClient(client);
        client.Start();
        node.Emit("Connecting...");
        return node;
    }

    public void Poll()
    {
        if (_finished)
        {
            return;
        }

        if (_host is not null)
        {
            _host.Poll();
            if (_host.Players.Count > 1 && _probe.TryCreateProbe(_probeBuffer))
            {
                _host.BroadcastEngineData(Channels.EngineUnreliable, _probeBuffer);
            }
        }
        else
        {
            _client!.Poll();
            if (_client.State == ClientState.Joined && _probe.TryCreateProbe(_probeBuffer))
            {
                _client.SendEngineData(Channels.EngineUnreliable, _probeBuffer);
            }
        }

        if (_options.ReportIntervalMs > 0 && _clock.NowMs >= _nextReportAt)
        {
            _nextReportAt = _clock.NowMs + _options.ReportIntervalMs;
            if (IsJoined && Players.Count > 1)
            {
                Emit(DescribeLink());
            }
        }
    }

    public void Say(string text)
    {
        if (_host is not null)
        {
            _host.SendChat(text);
        }
        else
        {
            _client!.SendChat(text);
        }
    }

    public string DescribePlayers() =>
        $"{Players.Count} player(s): " + string.Join(", ", Players.Select(p => $"{p.Name} ({p.Id})"));

    /// <summary>Round-trip time and probe loss per peer since the last report.</summary>
    public string DescribeLink()
    {
        var samples = _probe.TakeSamples().ToDictionary(s => s.Player);
        var parts = new List<string>();
        if (_host is not null)
        {
            foreach (var player in _host.Players.Where(p => p.Id != PlayerId.Host))
            {
                parts.Add(DescribePeer(player.Name, _host.GetRoundTripTimeMs(player.Id), samples.GetValueOrDefault(player.Id)));
            }
        }
        else if (_client!.State == ClientState.Joined)
        {
            var hostName = _client.Players.FirstOrDefault(p => p.Id == PlayerId.Host)?.Name ?? "host";
            parts.Add(DescribePeer(hostName, _client.RoundTripTimeMs, samples.GetValueOrDefault(PlayerId.Host)));
        }

        return parts.Count == 0 ? "Link: no peers." : "Link: " + string.Join("; ", parts);
    }

    public void Dispose()
    {
        if (_host is not null)
        {
            _host.Dispose();
        }
        else
        {
            _client!.Dispose();
        }

        _finished = true;
    }

    private static string DescribePeer(string name, int rttMs, LinkSample sample)
    {
        var rtt = rttMs >= 0 ? $"{rttMs} ms RTT" : "RTT unknown";
        var loss = sample.Expected > 0 ? $", {sample.LossPercent:0.0}% probe loss ({sample.Received}/{sample.Expected})" : ", no probes yet";
        return $"{name}: {rtt}{loss}";
    }

    private void WireHost(HostSession host)
    {
        host.PlayerJoined += player =>
        {
            Emit($"{player.Name} ({player.Id}) joined. {DescribePlayers()}");
            var greeting = string.IsNullOrWhiteSpace(_options.Greeting) ? string.Empty : $" This host is {_options.Greeting}.";
            host.SendChat($"Welcome, {player.Name}!{greeting}");
        };
        host.PlayerLeft += (player, reason) =>
        {
            _probe.Forget(player.Id);
            Emit($"{player.Name} ({player.Id}) left: {reason}.");
        };
        host.ChatReceived += OnChat;
        host.EngineDataReceived += (from, _, data) => _probe.TryReceive(from, data);
    }

    private void WireClient(ClientSession client)
    {
        client.Joined += () =>
        {
            Emit($"Joined \"{client.SessionName}\" as {client.LocalPlayerId}. {DescribePlayers()}");
            if (!string.IsNullOrWhiteSpace(_options.Greeting))
            {
                client.SendChat($"Hello from {_options.Name}, {_options.Greeting}.");
            }
        };
        client.PlayerJoined += player => Emit($"{player.Name} ({player.Id}) joined.");
        client.PlayerLeft += (player, reason) => Emit($"{player.Name} ({player.Id}) left: {reason}.");
        client.ChatReceived += OnChat;
        client.EngineDataReceived += (from, _, data) => _probe.TryReceive(from, data);
        client.Disconnected += (reason, message) =>
        {
            _finished = true;
            Emit($"Disconnected: {DisconnectReasonText.Describe(reason)}{(message is null ? string.Empty : " (" + message + ")")}");
        };
    }

    private void OnChat(PlayerInfo from, string text)
    {
        Emit($"<{from.Name}> {text}");
        ChatReceived?.Invoke(from, text);
    }

    private void Emit(string line) => Output?.Invoke(line);
}
