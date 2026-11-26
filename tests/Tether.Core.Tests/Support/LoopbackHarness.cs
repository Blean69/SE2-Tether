// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using Tether.Diagnostics;
using Tether.Protocol;
using Tether.Session;
using Tether.Transport;
using Tether.Transport.Loopback;

namespace Tether.Tests.Support;

/// <summary>A host plus any number of clients on one simulated network, driven by a manual clock.</summary>
internal sealed class LoopbackHarness : IDisposable
{
    public const int StepMs = 10;

    private readonly List<ClientSession> _clients = new();
    private readonly List<RawPeer> _raws = new();

    public LoopbackHarness(HostOptions? hostOptions = null, LoopbackConditions? conditions = null, int seed = 1)
    {
        Clock = new ManualClock(1_000);
        Network = new LoopbackNetwork(Clock, conditions, seed);
        HostOptions = hostOptions ?? new HostOptions { SessionName = "Test session", HostPlayerName = "Hosty", PasswordIterations = 1_000 };
        Host = new HostSession(Network.CreateHost(), HostOptions, Clock, Log);
        Host.Start();
    }

    public ManualClock Clock { get; }

    public LoopbackNetwork Network { get; }

    public HostOptions HostOptions { get; }

    public HostSession Host { get; }

    public TestLog Log { get; } = new();

    public ClientSession AddClient(string name = "Client", string? password = null, GameIdentity? game = null, bool start = true)
    {
        var client = new ClientSession(
            Network.CreateClient(),
            new ClientOptions { PlayerName = name, Password = password, Game = game ?? HostOptions.Game },
            Clock,
            Log);
        _clients.Add(client);
        if (start)
        {
            client.Start();
        }

        return client;
    }

    /// <summary>A transport-level peer that can send arbitrary bytes, to play a hostile or outdated client.</summary>
    public RawPeer AddRaw()
    {
        var raw = new RawPeer(Network.CreateClient());
        _raws.Add(raw);
        return raw;
    }

    /// <summary>Advances simulated time, polling everything every <see cref="StepMs"/>.</summary>
    public void Run(int ms)
    {
        for (var elapsed = 0; elapsed < ms; elapsed += StepMs)
        {
            Clock.Advance(StepMs);
            PollAll();
        }
    }

    public bool RunUntil(Func<bool> condition, int maxMs = 5_000)
    {
        for (var elapsed = 0; elapsed <= maxMs; elapsed += StepMs)
        {
            if (condition())
            {
                return true;
            }

            Clock.Advance(StepMs);
            PollAll();
        }

        return condition();
    }

    public void PollAll()
    {
        Host.Poll();
        foreach (var client in _clients.ToArray())
        {
            client.Poll();
        }

        foreach (var raw in _raws)
        {
            raw.Transport.Poll();
        }
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        foreach (var raw in _raws)
        {
            raw.Transport.Dispose();
        }

        Host.Dispose();
    }
}

/// <summary>Records everything a raw transport sees.</summary>
internal sealed class RawPeer : ITransportEvents
{
    public RawPeer(ITransport transport)
    {
        Transport = transport;
        transport.Start(this);
    }

    public ITransport Transport { get; }

    public TransportConnection? Connection { get; private set; }

    public List<(byte Channel, byte[] Payload)> Received { get; } = new();

    public DisconnectReason? DisconnectReason { get; private set; }

    public string? DisconnectMessage { get; private set; }

    public bool IsConnected => Connection is { IsConnected: true } && DisconnectReason is null;

    public SendResult Send(byte channel, ReadOnlySpan<byte> payload) =>
        Connection?.Send(channel, payload) ?? SendResult.NotConnected;

    public SendResult Send(byte channel, Action<PacketWriter> write)
    {
        using var writer = new PacketWriter();
        write(writer);
        return Send(channel, writer.WrittenSpan);
    }

    public void OnConnected(TransportConnection connection) => Connection = connection;

    public void OnDisconnected(TransportConnection connection, DisconnectReason reason, string? message)
    {
        DisconnectReason = reason;
        DisconnectMessage = message;
    }

    public void OnReceived(TransportConnection connection, byte channel, ReadOnlySpan<byte> payload) =>
        Received.Add((channel, payload.ToArray()));
}

internal sealed class TestLog : ITetherLog
{
    public List<string> Lines { get; } = new();

    public List<Exception> Errors { get; } = new();

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        lock (Lines)
        {
            Lines.Add($"{level}: {message}");
            if (level == LogLevel.Error)
            {
                Errors.Add(exception ?? new Exception(message));
            }
        }
    }
}
