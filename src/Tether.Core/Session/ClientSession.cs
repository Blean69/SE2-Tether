// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using Tether.Diagnostics;
using Tether.Protocol;
using Tether.Transport;

namespace Tether.Session;

public enum ClientState
{
    NotStarted,

    /// <summary>The transport is connecting to the host.</summary>
    Connecting,

    /// <summary>Connected; Hello sent, waiting for a Welcome (and maybe a password challenge).</summary>
    Joining,

    Joined,

    Disconnected,
}

/// <summary>
/// The client side of a co-op session: one connection to the host.
/// </summary>
/// <remarks>
/// Drive it from one thread. Call <see cref="Start"/> once, then <see cref="Poll"/> every frame. All
/// events are raised from inside <see cref="Poll"/>, except <see cref="Disconnected"/> when you call
/// <see cref="Leave"/> yourself. The session owns the transport.
/// </remarks>
public sealed class ClientSession : IDisposable
{
    private readonly ITransport _transport;
    private readonly ClientOptions _options;
    private readonly IClock _clock;
    private readonly ITetherLog _log;
    private readonly Events _events;
    private readonly PacketWriter _writer = new();
    private readonly SortedList<byte, PlayerInfo> _players = new();
    private TransportConnection? _connection;
    private long _deadline;
    private bool _answeredChallenge;
    private bool _polling;
    private bool _transportDisposed;

    public ClientSession(ITransport transport, ClientOptions options, IClock? clock = null, ITetherLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        _transport = transport;
        _options = options;
        _clock = clock ?? SystemClock.Instance;
        _log = log ?? NullLog.Instance;
        _events = new Events(this);
    }

    /// <summary>The host accepted us. <see cref="LocalPlayerId"/> and <see cref="Players"/> are now valid.</summary>
    public event Action? Joined;

    /// <summary>The session ended, for any reason, including <see cref="Leave"/>. Raised once.</summary>
    public event Action<DisconnectReason, string?>? Disconnected;

    /// <summary>Another player joined after us.</summary>
    public event Action<PlayerInfo>? PlayerJoined;

    public event Action<PlayerInfo, DisconnectReason>? PlayerLeft;

    /// <summary>A chat line, as attributed by the host. Includes our own lines echoed back.</summary>
    public event Action<PlayerInfo, string>? ChatReceived;

    /// <summary>Engine payload from the host. The sender is always <see cref="PlayerId.Host"/>.</summary>
    public event EngineDataHandler? EngineDataReceived;

    public ClientState State { get; private set; }

    public PlayerId LocalPlayerId { get; private set; }

    public string SessionName { get; private set; } = string.Empty;

    public int MaxPlayers { get; private set; }

    /// <summary>Everyone in the session, ordered by id (the host first).</summary>
    public IList<PlayerInfo> Players => _players.Values;

    /// <summary>Smoothed round-trip time to the host in milliseconds, or -1 if unknown.</summary>
    public int RoundTripTimeMs => State == ClientState.Joined ? _connection?.RoundTripTimeMs ?? -1 : -1;

    public DisconnectReason? EndReason { get; private set; }

    public string? EndMessage { get; private set; }

    public void Start()
    {
        if (State != ClientState.NotStarted)
        {
            throw new InvalidOperationException("Session already started.");
        }

        State = ClientState.Connecting;
        _deadline = _clock.NowMs + _options.JoinTimeoutMs;
        _transport.Start(_events);
        _log.Info($"[client] Connecting via {_transport.Name}...");
    }

    public void Poll()
    {
        if (State is ClientState.NotStarted or ClientState.Disconnected)
        {
            return;
        }

        _polling = true;
        try
        {
            _transport.Poll();
        }
        finally
        {
            _polling = false;
        }

        if (State is ClientState.Connecting or ClientState.Joining && _clock.NowMs > _deadline)
        {
            Fail(DisconnectReason.HandshakeTimeout, null);
        }

        if (State == ClientState.Disconnected)
        {
            ReleaseTransport();
        }
    }

    public SendResult SendEngineData(byte channel, ReadOnlySpan<byte> payload)
    {
        if (channel is not (Channels.EngineReliable or Channels.EngineUnreliable or Channels.Bulk))
        {
            return SendResult.InvalidChannel;
        }

        if (State != ClientState.Joined || _connection is null)
        {
            return SendResult.NotConnected;
        }

        _writer.Reset();
        EngineDataMessage.WriteTo(_writer, payload);
        return _connection.Send(channel, _writer.WrittenSpan);
    }

    /// <summary>Sends a chat line. It appears through <see cref="ChatReceived"/> once the host relays it.</summary>
    public void SendChat(string text)
    {
        var clean = TextRules.SanitizeChat(text);
        if (clean.Length == 0 || State != ClientState.Joined || _connection is null)
        {
            return;
        }

        _writer.Reset();
        new ChatSendMessage(clean).WriteTo(_writer);
        _connection.Send(Channels.Control, _writer.WrittenSpan);
    }

    /// <summary>Leaves the session and releases the transport.</summary>
    public void Leave()
    {
        if (State == ClientState.Disconnected)
        {
            return;
        }

        var started = State != ClientState.NotStarted;
        _connection?.Disconnect(DisconnectReason.ClientLeft);
        End(DisconnectReason.ClientLeft, null, raise: started);
    }

    public void Dispose() => Leave();

    private void OnConnected(TransportConnection connection)
    {
        if (State != ClientState.Connecting || _connection is not null)
        {
            connection.Disconnect(DisconnectReason.ProtocolViolation);
            return;
        }

        _connection = connection;
        State = ClientState.Joining;
        _log.Info($"[client] Connected to {connection.RemoteDescription}; joining.");

        _writer.Reset();
        new HelloMessage(
            ProtocolConstants.ProtocolVersion,
            Truncate(TetherVersion.Plugin, ProtocolConstants.MaxVersionStringBytes),
            _options.Game.GameBuild,
            _options.Game.ModFingerprint,
            TextRules.SanitizePlayerName(_options.PlayerName),
            PeerCapabilities.None).WriteTo(_writer);
        connection.Send(Channels.Control, _writer.WrittenSpan);
    }

    private void OnDisconnected(TransportConnection connection, DisconnectReason reason, string? message)
    {
        if (State == ClientState.Disconnected)
        {
            return;
        }

        if (_connection is not null && !ReferenceEquals(connection, _connection))
        {
            return;
        }

        _log.Info($"[client] Disconnected: {reason}{(message is null ? string.Empty : " - " + message)}.");
        End(reason, message, raise: true);
    }

    private void OnReceived(TransportConnection connection, byte channel, ReadOnlySpan<byte> payload)
    {
        if (!ReferenceEquals(connection, _connection) || State is not (ClientState.Joining or ClientState.Joined))
        {
            return;
        }

        try
        {
            if (payload.IsEmpty)
            {
                throw new ProtocolViolationException("Empty message.");
            }

            var reader = new PacketReader(payload);
            var type = (MessageType)reader.ReadByte();
            if (State == ClientState.Joining)
            {
                HandleJoining(type, channel, ref reader);
            }
            else
            {
                HandleJoined(type, channel, ref reader);
            }
        }
        catch (ProtocolViolationException ex)
        {
            _log.Warn($"[client] Protocol violation from the host: {ex.Message}");
            Fail(DisconnectReason.ProtocolViolation, null);
        }
    }

    private void HandleJoining(MessageType type, byte channel, ref PacketReader reader)
    {
        if (channel != Channels.Control)
        {
            throw new ProtocolViolationException($"Unexpected {type} on channel {channel} while joining.");
        }

        switch (type)
        {
            case MessageType.AuthChallenge when !_answeredChallenge:
                var challenge = AuthChallengeMessage.ReadBody(ref reader);
                _answeredChallenge = true;
                if (string.IsNullOrEmpty(_options.Password))
                {
                    Fail(DisconnectReason.PasswordRequired, null);
                    return;
                }

                var key = PasswordProof.DeriveKey(_options.Password, challenge.Salt, challenge.Iterations);
                _writer.Reset();
                new AuthResponseMessage(PasswordProof.ComputeMac(key, challenge.Nonce)).WriteTo(_writer);
                _connection!.Send(Channels.Control, _writer.WrittenSpan);
                break;

            case MessageType.Welcome:
                var welcome = WelcomeMessage.ReadBody(ref reader);
                LocalPlayerId = welcome.YourId;
                SessionName = welcome.SessionName;
                MaxPlayers = welcome.MaxPlayers;
                _players.Clear();
                foreach (var player in welcome.Players)
                {
                    _players[player.Id.Value] = player;
                }

                State = ClientState.Joined;
                _log.Info($"[client] Joined '{SessionName}' as {LocalPlayerId} with {_players.Count} player(s).");
                Raise(() => Joined?.Invoke(), nameof(Joined));
                break;

            default:
                throw new ProtocolViolationException($"Unexpected {type} while joining.");
        }
    }

    private void HandleJoined(MessageType type, byte channel, ref PacketReader reader)
    {
        if (type == MessageType.EngineData)
        {
            if (channel is not (Channels.EngineReliable or Channels.EngineUnreliable or Channels.Bulk))
            {
                throw new ProtocolViolationException($"Engine data on channel {channel}.");
            }

            var body = reader.ReadToEnd();
            var handler = EngineDataReceived;
            if (handler is not null)
            {
                try
                {
                    handler(PlayerId.Host, channel, body);
                }
                catch (Exception ex)
                {
                    _log.Error("[client] EngineDataReceived handler threw.", ex);
                }
            }

            return;
        }

        if (channel != Channels.Control)
        {
            throw new ProtocolViolationException($"Unexpected {type} on channel {channel}.");
        }

        switch (type)
        {
            case MessageType.PlayerJoined:
                var joined = PlayerJoinedMessage.ReadBody(ref reader).Player;
                if (joined.Id == LocalPlayerId)
                {
                    throw new ProtocolViolationException("Host announced our own id as a new player.");
                }

                _players[joined.Id.Value] = joined;
                Raise(() => PlayerJoined?.Invoke(joined), nameof(PlayerJoined));
                break;

            case MessageType.PlayerLeft:
                var left = PlayerLeftMessage.ReadBody(ref reader);
                if (_players.Remove(left.Player.Value, out var leftPlayer))
                {
                    Raise(() => PlayerLeft?.Invoke(leftPlayer, left.Reason), nameof(PlayerLeft));
                }

                break;

            case MessageType.ChatMessage:
                var chat = ChatMessage.ReadBody(ref reader);
                var text = TextRules.SanitizeChat(chat.Text);
                if (text.Length > 0)
                {
                    var from = _players.TryGetValue(chat.From.Value, out var known)
                        ? known
                        : new PlayerInfo(chat.From, $"Player {chat.From}");
                    Raise(() => ChatReceived?.Invoke(from, text), nameof(ChatReceived));
                }

                break;

            default:
                throw new ProtocolViolationException($"Unexpected {type} after joining.");
        }
    }

    private void Fail(DisconnectReason reason, string? message)
    {
        _log.Info($"[client] Leaving: {reason}.");
        _connection?.Disconnect(reason, message);
        End(reason, message, raise: true);
    }

    private void End(DisconnectReason reason, string? message, bool raise)
    {
        if (State == ClientState.Disconnected)
        {
            return;
        }

        State = ClientState.Disconnected;
        EndReason = reason;
        EndMessage = message;
        if (!_polling)
        {
            // Inside Poll() the transport is still running its own loop; Poll() releases it on the way out.
            ReleaseTransport();
        }

        if (raise)
        {
            Raise(() => Disconnected?.Invoke(reason, message), nameof(Disconnected));
        }
    }

    private void ReleaseTransport()
    {
        if (_transportDisposed)
        {
            return;
        }

        _transportDisposed = true;
        _transport.Dispose();
        _writer.Dispose();
    }

    private void Raise(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.Error($"[client] {name} handler threw.", ex);
        }
    }

    private static string Truncate(string value, int maxBytes)
    {
        while (System.Text.Encoding.UTF8.GetByteCount(value) > maxBytes)
        {
            value = value.Substring(0, value.Length - 1);
        }

        return value;
    }

    private sealed class Events : ITransportEvents
    {
        private readonly ClientSession _session;

        public Events(ClientSession session)
        {
            _session = session;
        }

        public void OnConnected(TransportConnection connection) => _session.OnConnected(connection);

        public void OnDisconnected(TransportConnection connection, DisconnectReason reason, string? message) =>
            _session.OnDisconnected(connection, reason, message);

        public void OnReceived(TransportConnection connection, byte channel, ReadOnlySpan<byte> payload) =>
            _session.OnReceived(connection, channel, payload);
    }
}
