// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using Tether.Diagnostics;
using Tether.Protocol;

namespace Tether.Transport.Loopback;

/// <summary>Simulated network conditions for a <see cref="LoopbackNetwork"/>.</summary>
public sealed record LoopbackConditions
{
    public static readonly LoopbackConditions Perfect = new();

    /// <summary>One-way base latency in milliseconds.</summary>
    public int LatencyMs { get; init; }

    /// <summary>Extra random one-way delay, from 0 to this value, in milliseconds.</summary>
    public int JitterMs { get; init; }

    /// <summary>Chance from 0 to 1 that an unreliable message is dropped. Reliable channels are never lost.</summary>
    public double UnreliableLoss { get; init; }

    /// <summary>
    /// Chance from 0 to 1 that an unreliable message ignores link order, so it can overtake or fall behind
    /// its neighbours. Otherwise a link is FIFO, as real paths almost always are; jitter delays packets
    /// without shuffling them.
    /// </summary>
    public double UnreliableReorder { get; init; }
}

/// <summary>
/// An in-memory network for tests and simulations. It is deterministic for a given seed and clock.
/// Create one host with <see cref="CreateHost"/> and any number of clients with <see cref="CreateClient"/>.
/// </summary>
public sealed class LoopbackNetwork
{
    public const int MaxUnreliablePayload = 1200;

    private readonly Dictionary<string, LoopbackTransport> _hosts = new(StringComparer.Ordinal);
    private readonly Random _random;
    private long _sequence;

    public LoopbackNetwork(IClock clock, LoopbackConditions? conditions = null, int seed = 1)
    {
        Clock = clock;
        Conditions = conditions ?? LoopbackConditions.Perfect;
        _random = new Random(seed);
    }

    public IClock Clock { get; }

    /// <summary>Can be changed mid-test, for example to inject a lag spike.</summary>
    public LoopbackConditions Conditions { get; set; }

    public ITransport CreateHost(string address = "host") => new LoopbackTransport(this, address, hostAddress: null);

    public ITransport CreateClient(string hostAddress = "host", string? clientAddress = null) =>
        new LoopbackTransport(this, clientAddress ?? $"client-{Guid.NewGuid():N}", hostAddress);

    internal long NextSequence() => ++_sequence;

    internal long OneWayDelay()
    {
        var c = Conditions;
        var jitter = c.JitterMs > 0 ? _random.Next(0, c.JitterMs + 1) : 0;
        return c.LatencyMs + jitter;
    }

    internal bool DropUnreliable() => Conditions.UnreliableLoss > 0 && _random.NextDouble() < Conditions.UnreliableLoss;

    internal bool ReorderUnreliable() => Conditions.UnreliableReorder > 0 && _random.NextDouble() < Conditions.UnreliableReorder;

    internal void RegisterHost(LoopbackTransport host)
    {
        if (!_hosts.TryAdd(host.Address, host))
        {
            throw new InvalidOperationException($"A loopback host is already listening at '{host.Address}'.");
        }
    }

    internal void UnregisterHost(LoopbackTransport host)
    {
        if (_hosts.TryGetValue(host.Address, out var current) && ReferenceEquals(current, host))
        {
            _hosts.Remove(host.Address);
        }
    }

    internal LoopbackTransport? FindHost(string address) => _hosts.TryGetValue(address, out var host) ? host : null;
}

internal sealed class LoopbackTransport : ITransport
{
    private readonly LoopbackNetwork _network;
    private readonly string? _hostAddress;
    private readonly PriorityQueue<Event, (long DeliverAt, long Sequence)> _inbox = new();
    private readonly List<LoopbackConnection> _connections = new();
    private ITransportEvents? _events;
    private bool _disposed;

    public LoopbackTransport(LoopbackNetwork network, string address, string? hostAddress)
    {
        _network = network;
        Address = address;
        _hostAddress = hostAddress;
    }

    public string Name => "loopback";

    public string Address { get; }

    private bool IsHost => _hostAddress is null;

    public void Start(ITransportEvents events)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_events is not null)
        {
            throw new InvalidOperationException("Transport already started.");
        }

        _events = events;
        if (IsHost)
        {
            _network.RegisterHost(this);
            return;
        }

        var now = _network.Clock.NowMs;
        var host = _network.FindHost(_hostAddress!);
        var local = new LoopbackConnection(this, _hostAddress!);
        _connections.Add(local);
        if (host is null || host._disposed)
        {
            Schedule(now + _network.OneWayDelay() * 2, Event.Disconnected(local, DisconnectReason.ConnectionFailed, "No host at that address."));
            return;
        }

        var remote = new LoopbackConnection(host, Address);
        host._connections.Add(remote);
        local.Peer = remote;
        remote.Peer = local;

        // The host learns about the connection after one trip; the client after the full handshake round trip.
        var toHost = now + _network.OneWayDelay();
        host.Schedule(toHost, Event.Connected(remote));
        Schedule(toHost + _network.OneWayDelay(), Event.Connected(local));
    }

    public void Poll()
    {
        if (_disposed || _events is null)
        {
            return;
        }

        var now = _network.Clock.NowMs;
        while (_inbox.TryPeek(out var evt, out var key) && key.DeliverAt <= now)
        {
            _inbox.Dequeue();
            Dispatch(evt);
            if (_disposed)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var connection in _connections.ToArray())
        {
            connection.CloseFromLocal(DisconnectReason.Unknown, null, raiseLocally: false);
        }

        _connections.Clear();
        _inbox.Clear();
        if (IsHost)
        {
            _network.UnregisterHost(this);
        }

        _disposed = true;
    }

    internal LoopbackNetwork Network => _network;

    internal bool IsDisposed => _disposed;

    internal void Schedule(long deliverAt, Event evt)
    {
        if (!_disposed)
        {
            _inbox.Enqueue(evt, (deliverAt, _network.NextSequence()));
        }
    }

    internal void Remove(LoopbackConnection connection) => _connections.Remove(connection);

    private void Dispatch(Event evt)
    {
        var connection = evt.Connection;
        switch (evt.Kind)
        {
            case EventKind.Connected:
                if (connection.State == ConnState.Connecting)
                {
                    connection.State = ConnState.Connected;
                    _events!.OnConnected(connection);
                }

                break;

            case EventKind.Data:
                if (connection.State == ConnState.Connecting)
                {
                    // Jitter can deliver the host's first message before our own connect event.
                    // Real transports always report the connection first, so do the same.
                    connection.State = ConnState.Connected;
                    _events!.OnConnected(connection);
                    if (_disposed || connection.State != ConnState.Connected)
                    {
                        break;
                    }
                }

                if (connection.State == ConnState.Connected && connection.AcceptSequenced(evt.Channel, evt.ChannelSequence))
                {
                    _events!.OnReceived(connection, evt.Channel, evt.Payload);
                }

                break;

            case EventKind.Disconnected:
                if (!connection.DisconnectRaised)
                {
                    connection.State = ConnState.Closed;
                    connection.DisconnectRaised = true;
                    Remove(connection);
                    _events!.OnDisconnected(connection, evt.Reason, evt.Message);
                }

                break;
        }
    }

    internal enum EventKind
    {
        Connected,
        Data,
        Disconnected,
    }

    internal sealed class Event
    {
        public EventKind Kind { get; private init; }

        public LoopbackConnection Connection { get; private init; } = null!;

        public byte Channel { get; private init; }

        public long ChannelSequence { get; private init; }

        public byte[] Payload { get; private init; } = Array.Empty<byte>();

        public DisconnectReason Reason { get; private init; }

        public string? Message { get; private init; }

        public static Event Connected(LoopbackConnection c) => new() { Kind = EventKind.Connected, Connection = c };

        public static Event Data(LoopbackConnection c, byte channel, long channelSequence, byte[] payload) =>
            new() { Kind = EventKind.Data, Connection = c, Channel = channel, ChannelSequence = channelSequence, Payload = payload };

        public static Event Disconnected(LoopbackConnection c, DisconnectReason reason, string? message) =>
            new() { Kind = EventKind.Disconnected, Connection = c, Reason = reason, Message = message };
    }
}

internal enum ConnState
{
    Connecting,
    Connected,
    Closed,
}

internal sealed class LoopbackConnection : TransportConnection
{
    private readonly LoopbackTransport _owner;
    private readonly long[] _lastDeliverAt = new long[Channels.Count];
    private readonly long[] _sendSequence = new long[Channels.Count];
    private readonly long[] _lastAcceptedSequence = new long[Channels.Count];
    private readonly string _remoteDescription;

    public LoopbackConnection(LoopbackTransport owner, string remoteDescription)
    {
        _owner = owner;
        _remoteDescription = "loopback:" + remoteDescription;
    }

    public LoopbackConnection? Peer { get; set; }

    public ConnState State { get; set; }

    public bool DisconnectRaised { get; set; }

    public override string RemoteDescription => _remoteDescription;

    public override bool IsConnected => State == ConnState.Connected;

    public override int RoundTripTimeMs => _owner.Network.Conditions.LatencyMs * 2;

    public override int MaxUnreliablePayload => LoopbackNetwork.MaxUnreliablePayload;

    public override SendResult Send(byte channel, ReadOnlySpan<byte> payload)
    {
        if (!Channels.IsValid(channel))
        {
            return SendResult.InvalidChannel;
        }

        if (State != ConnState.Connected || Peer is null || Peer.State == ConnState.Closed || Peer._owner.IsDisposed)
        {
            return SendResult.NotConnected;
        }

        var delivery = Channels.DeliveryOf(channel);
        var limit = delivery == Delivery.UnreliableSequenced ? MaxUnreliablePayload : ProtocolConstants.MaxReliablePayload;
        if (payload.Length > limit)
        {
            return SendResult.TooLarge;
        }

        var network = _owner.Network;
        var sequence = ++_sendSequence[channel];
        var deliverAt = network.Clock.NowMs + network.OneWayDelay();
        if (delivery == Delivery.UnreliableSequenced && network.DropUnreliable())
        {
            return SendResult.Sent;
        }

        if (delivery == Delivery.ReliableOrdered || !network.ReorderUnreliable())
        {
            // Links are FIFO unless told to reorder; reliable channels always keep send order.
            deliverAt = Math.Max(deliverAt, _lastDeliverAt[channel]);
            _lastDeliverAt[channel] = deliverAt;
        }

        Peer._owner.Schedule(deliverAt, LoopbackTransport.Event.Data(Peer, channel, sequence, payload.ToArray()));
        return SendResult.Sent;
    }

    public override void Disconnect(DisconnectReason reason, string? message = null) =>
        CloseFromLocal(reason, message, raiseLocally: true);

    internal void CloseFromLocal(DisconnectReason reason, string? message, bool raiseLocally)
    {
        if (State == ConnState.Closed)
        {
            return;
        }

        State = ConnState.Closed;
        var network = _owner.Network;
        var now = network.Clock.NowMs;
        if (raiseLocally)
        {
            // Raised on the next Poll, never re-entrantly.
            _owner.Schedule(now, LoopbackTransport.Event.Disconnected(this, reason, message));
        }
        else
        {
            DisconnectRaised = true;
            _owner.Remove(this);
        }

        var peer = Peer;
        if (peer is not null && peer.State != ConnState.Closed)
        {
            // Deliver after anything reliable already in flight, as a graceful close would.
            var deliverAt = now + network.OneWayDelay();
            foreach (var t in _lastDeliverAt)
            {
                deliverAt = Math.Max(deliverAt, t);
            }

            var cleaned = message is null ? null : TextRules.SanitizeDisconnectMessage(message);
            peer._owner.Schedule(deliverAt, LoopbackTransport.Event.Disconnected(peer, reason, cleaned));
        }
    }

    /// <summary>Sequenced delivery: drops messages older than the newest already delivered on that channel.</summary>
    internal bool AcceptSequenced(byte channel, long sequence)
    {
        if (Channels.DeliveryOf(channel) != Delivery.UnreliableSequenced)
        {
            return true;
        }

        if (sequence <= _lastAcceptedSequence[channel])
        {
            return false;
        }

        _lastAcceptedSequence[channel] = sequence;
        return true;
    }
}
