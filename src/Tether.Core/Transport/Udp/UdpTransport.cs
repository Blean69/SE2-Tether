// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using LiteNetLib;
using LiteNetLib.Utils;
using Tether.Diagnostics;
using Tether.Protocol;
using DisconnectReason = Tether.Protocol.DisconnectReason;
using LnlDisconnectReason = LiteNetLib.DisconnectReason;

namespace Tether.Transport.Udp;

/// <summary>Options for <see cref="UdpTransport"/>.</summary>
public sealed record UdpTransportOptions
{
    public const int DefaultPort = 27027;

    /// <summary>Connection dropped after this long without traffic.</summary>
    public int DisconnectTimeoutMs { get; init; } = 10_000;

    /// <summary>Transport-level cap on simultaneous connections (host only). The session enforces the player limit.</summary>
    public int MaxConnections { get; init; } = ProtocolConstants.MaxPlayers;

    public bool EnableIPv6 { get; init; } = true;

    /// <summary>Test aid: LiteNetLib's built-in latency simulation, as a range in milliseconds.</summary>
    public (int Min, int Max)? SimulatedLatencyMs { get; init; }

    /// <summary>Test aid: LiteNetLib's built-in packet loss simulation, from 0 to 100 percent.</summary>
    public int? SimulatedPacketLossPercent { get; init; }
}

/// <summary>
/// Direct UDP transport built on LiteNetLib. Used for LAN play, direct-IP joins and local
/// development, for example two game instances on one PC. Hosting from home needs a forwarded UDP port;
/// the Steam transport avoids that.
/// </summary>
/// <remarks>Traffic is not encrypted. See <see cref="PasswordProof"/> for what is and isn't protected.</remarks>
public sealed class UdpTransport : ITransport
{
    /// <summary>Filters stray traffic at connect time. The real compatibility check is the Hello handshake.</summary>
    internal const string ConnectionKey = "Tether";

    private readonly UdpTransportOptions _options;
    private readonly ITetherLog _log;
    private readonly string? _remoteHost;
    private readonly int _port;
    private readonly Listener _listener;
    private readonly NetManager _manager;
    private readonly Dictionary<NetPeer, UdpConnection> _connections = new();
    private readonly List<UdpConnection> _pendingLocalDisconnects = new();
    private ITransportEvents? _events;
    private UdpConnection? _clientConnection;
    private bool _disposed;

    private UdpTransport(string? remoteHost, int port, UdpTransportOptions? options, ITetherLog? log)
    {
        _remoteHost = remoteHost;
        _port = port;
        _options = options ?? new UdpTransportOptions();
        _log = log ?? NullLog.Instance;
        _listener = new Listener(this);
        _manager = new NetManager(_listener)
        {
            ChannelsCount = Channels.Count,
            AutoRecycle = true,
            UnsyncedEvents = false,
            DisconnectTimeout = _options.DisconnectTimeoutMs,
            IPv6Enabled = _options.EnableIPv6,
        };

        if (_options.SimulatedLatencyMs is { } latency)
        {
            _manager.SimulateLatency = true;
            _manager.SimulationMinLatency = latency.Min;
            _manager.SimulationMaxLatency = latency.Max;
        }

        if (_options.SimulatedPacketLossPercent is { } loss)
        {
            _manager.SimulatePacketLoss = true;
            _manager.SimulationPacketLossChance = loss;
        }
    }

    /// <summary>A host that listens on <paramref name="port"/>. Pass 0 for an ephemeral port, which is useful in tests.</summary>
    public static UdpTransport CreateHost(int port = UdpTransportOptions.DefaultPort, UdpTransportOptions? options = null, ITetherLog? log = null) =>
        new(null, port, options, log);

    public static UdpTransport CreateClient(string host, int port = UdpTransportOptions.DefaultPort, UdpTransportOptions? options = null, ITetherLog? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return new UdpTransport(host, port, options, log);
    }

    public string Name => "udp";

    /// <summary>The bound local port, available after <see cref="Start"/>.</summary>
    public int LocalPort => _manager.LocalPort;

    private bool IsHost => _remoteHost is null;

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
            if (!_manager.Start(_port))
            {
                throw new InvalidOperationException($"Could not listen on UDP port {_port}. Is it already in use?");
            }

            _log.Info($"[udp] Listening on port {_manager.LocalPort}.");
            return;
        }

        if (!_manager.Start())
        {
            throw new InvalidOperationException("Could not open a UDP socket.");
        }

        var description = $"{_remoteHost}:{_port}";
        NetPeer? peer = null;
        try
        {
            peer = _manager.Connect(_remoteHost!, _port, ConnectionKey);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException or FormatException)
        {
            _log.Warn($"[udp] Could not resolve or reach {description}.", ex);
        }

        _clientConnection = new UdpConnection(this, peer, description);
        if (peer is null)
        {
            QueueLocalDisconnect(_clientConnection, DisconnectReason.ConnectionFailed, "Could not resolve the host address.");
            return;
        }

        _connections[peer] = _clientConnection;
    }

    public void Poll()
    {
        if (_disposed || _events is null)
        {
            return;
        }

        _manager.PollEvents();

        if (_pendingLocalDisconnects.Count > 0)
        {
            var pending = _pendingLocalDisconnects.ToArray();
            _pendingLocalDisconnects.Clear();
            foreach (var connection in pending)
            {
                RaiseDisconnected(connection, connection.LocalReason, connection.LocalMessage);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var connection in _connections.Values)
        {
            connection.MarkClosed();
        }

        _connections.Clear();
        _pendingLocalDisconnects.Clear();
        _manager.Stop(true);
    }

    internal NetManager Manager => _manager;

    internal void QueueLocalDisconnect(UdpConnection connection, DisconnectReason reason, string? message)
    {
        connection.LocalReason = reason;
        connection.LocalMessage = message;
        connection.LocalDisconnectQueued = true;
        _pendingLocalDisconnects.Add(connection);
    }

    internal static byte[] EncodeDisconnect(DisconnectReason reason, string? message)
    {
        var text = message is null ? string.Empty : TextRules.SanitizeDisconnectMessage(message);
        var bytes = new byte[1 + Encoding.UTF8.GetByteCount(text)];
        bytes[0] = (byte)reason;
        Encoding.UTF8.GetBytes(text, 0, text.Length, bytes, 1);
        return bytes;
    }

    internal static (DisconnectReason Reason, string? Message) DecodeDisconnect(ReadOnlySpan<byte> data, DisconnectReason fallback)
    {
        if (data.IsEmpty)
        {
            return (fallback, null);
        }

        var reason = (DisconnectReason)data[0];
        if (!Enum.IsDefined(reason))
        {
            reason = DisconnectReason.Unknown;
        }

        var text = data.Length > 1 && data.Length <= 1 + ProtocolConstants.MaxDisconnectMessageBytes
            ? TextRules.SanitizeDisconnectMessage(Encoding.UTF8.GetString(data.Slice(1)))
            : null;
        return (reason, string.IsNullOrEmpty(text) ? null : text);
    }

    private void RaiseDisconnected(UdpConnection connection, DisconnectReason reason, string? message)
    {
        if (connection.DisconnectRaised)
        {
            return;
        }

        connection.DisconnectRaised = true;
        connection.MarkClosed();
        if (connection.Peer is not null)
        {
            _connections.Remove(connection.Peer);
        }

        _events!.OnDisconnected(connection, reason, message);
    }

    private static DisconnectReason MapReason(LnlDisconnectReason reason) => reason switch
    {
        LnlDisconnectReason.Timeout => DisconnectReason.Timeout,
        LnlDisconnectReason.ConnectionFailed or LnlDisconnectReason.HostUnreachable or LnlDisconnectReason.NetworkUnreachable
            or LnlDisconnectReason.UnknownHost or LnlDisconnectReason.InvalidProtocol => DisconnectReason.ConnectionFailed,
        LnlDisconnectReason.ConnectionRejected => DisconnectReason.ConnectionFailed,
        LnlDisconnectReason.RemoteConnectionClose => DisconnectReason.Unknown,
        _ => DisconnectReason.TransportError,
    };

    /// <summary>LiteNetLib callbacks. These run inside NetManager.PollEvents, on the polling thread.</summary>
    private sealed class Listener : INetEventListener
    {
        private readonly UdpTransport _owner;

        public Listener(UdpTransport owner)
        {
            _owner = owner;
        }

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (!_owner.IsHost || _owner._disposed)
            {
                request.Reject();
                return;
            }

            if (_owner._manager.ConnectedPeersCount >= _owner._options.MaxConnections)
            {
                request.Reject(EncodeDisconnect(DisconnectReason.SessionFull, null));
                return;
            }

            request.AcceptIfKey(ConnectionKey);
        }

        public void OnPeerConnected(NetPeer peer)
        {
            if (_owner._disposed)
            {
                return;
            }

            if (_owner.IsHost)
            {
                var connection = new UdpConnection(_owner, peer, peer.ToString() ?? "udp-peer");
                connection.MarkConnected();
                _owner._connections[peer] = connection;
                _owner._events!.OnConnected(connection);
                return;
            }

            if (_owner._connections.TryGetValue(peer, out var client) && !client.IsClosed)
            {
                client.MarkConnected();
                _owner._events!.OnConnected(client);
            }
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (_owner._disposed || !_owner._connections.TryGetValue(peer, out var connection) || connection.LocalDisconnectQueued)
            {
                // A local Disconnect() already queued the event with the reason we chose.
                return;
            }

            var fallback = MapReason(disconnectInfo.Reason);
            var reason = fallback;
            string? message = null;
            if (disconnectInfo.Reason is LnlDisconnectReason.RemoteConnectionClose or LnlDisconnectReason.ConnectionRejected
                && disconnectInfo.AdditionalData is { AvailableBytes: > 0 } data)
            {
                (reason, message) = DecodeDisconnect(data.GetRemainingBytesSpan(), fallback);
            }

            _owner.RaiseDisconnected(connection, reason, message);
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            if (_owner._disposed || !_owner._connections.TryGetValue(peer, out var connection) || !connection.IsConnected)
            {
                return;
            }

            if (!Channels.IsValid(channelNumber))
            {
                return;
            }

            var payload = reader.GetRemainingBytesSpan();
            if (payload.Length > ProtocolConstants.MaxReliablePayload)
            {
                // LiteNetLib reassembles fragments without a size cap. Refuse anything a well-behaved peer never sends.
                _owner._log.Warn($"[udp] {connection} sent an oversized message ({payload.Length} bytes); disconnecting.");
                connection.Disconnect(DisconnectReason.ProtocolViolation, "Message too large.");
                return;
            }

            _owner._events!.OnReceived(connection, channelNumber, payload);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) =>
            _owner._log.Warn($"[udp] Socket error {socketError} with {endPoint}.");

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
        {
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
        }
    }
}

internal sealed class UdpConnection : TransportConnection
{
    private readonly UdpTransport _owner;
    private readonly string _description;
    private bool _connected;
    private bool _closed;

    public UdpConnection(UdpTransport owner, NetPeer? peer, string description)
    {
        _owner = owner;
        Peer = peer;
        _description = "udp:" + description;
    }

    public NetPeer? Peer { get; }

    public bool DisconnectRaised { get; set; }

    public bool LocalDisconnectQueued { get; set; }

    public DisconnectReason LocalReason { get; set; }

    public string? LocalMessage { get; set; }

    public bool IsClosed => _closed;

    public override string RemoteDescription => _description;

    public override bool IsConnected => _connected && !_closed;

    public override int RoundTripTimeMs => Peer is null || !_connected ? -1 : Peer.RoundTripTime;

    public override int MaxUnreliablePayload =>
        Peer is null ? 0 : Peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced);

    public override SendResult Send(byte channel, ReadOnlySpan<byte> payload)
    {
        if (!Channels.IsValid(channel))
        {
            return SendResult.InvalidChannel;
        }

        if (!IsConnected || Peer is null)
        {
            return SendResult.NotConnected;
        }

        var delivery = Channels.DeliveryOf(channel);
        var method = delivery == Delivery.UnreliableSequenced ? DeliveryMethod.Sequenced : DeliveryMethod.ReliableOrdered;
        var limit = delivery == Delivery.UnreliableSequenced ? MaxUnreliablePayload : ProtocolConstants.MaxReliablePayload;
        if (payload.Length > limit)
        {
            return SendResult.TooLarge;
        }

        try
        {
            Peer.Send(payload, channel, method);
            return SendResult.Sent;
        }
        catch (TooBigPacketException)
        {
            return SendResult.TooLarge;
        }
        catch (InvalidPacketException)
        {
            return SendResult.Failed;
        }
    }

    public override void Disconnect(DisconnectReason reason, string? message = null)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        if (Peer is not null)
        {
            _owner.Manager.DisconnectPeer(Peer, UdpTransport.EncodeDisconnect(reason, message));
        }

        _owner.QueueLocalDisconnect(this, reason, message);
    }

    internal void MarkConnected() => _connected = true;

    internal void MarkClosed() => _closed = true;
}
