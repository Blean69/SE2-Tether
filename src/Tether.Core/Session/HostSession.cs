// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.Linq;
using Tether.Diagnostics;
using Tether.Protocol;
using Tether.Transport;

namespace Tether.Session;

/// <summary>
/// The host side of a co-op session. The host is the authority: it admits players, assigns their ids,
/// relays chat, and exchanges engine data with each client.
/// </summary>
/// <remarks>
/// <para>
/// Drive it from one thread, normally the game's main thread. Call <see cref="Start"/> once, then
/// <see cref="Poll"/> every frame. All events are raised from inside <see cref="Poll"/>.
/// </para>
/// <para>
/// Every byte from a client is treated as hostile. A malformed message, an unexpected message, or
/// exceeding the rate limits disconnects that client, and never affects the host or other players.
/// </para>
/// <para>The session owns the transport and disposes it on <see cref="Stop"/>.</para>
/// </remarks>
public sealed class HostSession : IDisposable
{
    private readonly ITransport _transport;
    private readonly HostOptions _options;
    private readonly IClock _clock;
    private readonly ITetherLog _log;
    private readonly Events _events;
    private readonly Dictionary<long, Peer> _peers = new();
    private readonly List<PlayerInfo> _players = new();
    private readonly PacketWriter _writer = new();
    private readonly byte[]? _passwordKey;
    private readonly byte[] _passwordSalt;
    private State _state;
    private bool _polling;
    private bool _transportDisposed;

    public HostSession(ITransport transport, HostOptions options, IClock? clock = null, ITetherLog? log = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxPlayers < 2 || options.MaxPlayers > ProtocolConstants.MaxPlayers)
        {
            throw new ArgumentOutOfRangeException(nameof(options), $"MaxPlayers must be between 2 and {ProtocolConstants.MaxPlayers}.");
        }

        _transport = transport;
        _options = options;
        _clock = clock ?? SystemClock.Instance;
        _log = log ?? NullLog.Instance;
        _events = new Events(this);

        SessionName = TextRules.SanitizeSessionName(options.SessionName, "Tether session");
        LocalPlayer = new PlayerInfo(PlayerId.Host, TextRules.SanitizePlayerName(options.HostPlayerName));
        _players.Add(LocalPlayer);

        _passwordSalt = PasswordProof.CreateRandom(ProtocolConstants.AuthSaltSize);
        if (!string.IsNullOrEmpty(options.Password))
        {
            // Derived once per session; each join then only costs an HMAC on the host.
            _passwordKey = PasswordProof.DeriveKey(options.Password, _passwordSalt, options.PasswordIterations);
        }
    }

    private enum State
    {
        Created,
        Running,
        Stopped,
    }

    private enum Phase
    {
        AwaitingHello,
        AwaitingAuth,
        Joined,
        Closed,
    }

    /// <summary>A player completed the handshake. Not raised for the host itself.</summary>
    public event Action<PlayerInfo>? PlayerJoined;

    /// <summary>A joined player left or was removed.</summary>
    public event Action<PlayerInfo, DisconnectReason>? PlayerLeft;

    /// <summary>A chat line from any player, including the host's own lines.</summary>
    public event Action<PlayerInfo, string>? ChatReceived;

    /// <summary>Engine payload from a joined client.</summary>
    public event EngineDataHandler? EngineDataReceived;

    public string SessionName { get; }

    public PlayerInfo LocalPlayer { get; }

    public int MaxPlayers => _options.MaxPlayers;

    public bool IsRunning => _state == State.Running;

    /// <summary>Everyone in the session, host first.</summary>
    public IReadOnlyList<PlayerInfo> Players => _players;

    public bool RequiresPassword => _passwordKey is not null;

    public void Start()
    {
        if (_state != State.Created)
        {
            throw new InvalidOperationException("Session already started.");
        }

        _state = State.Running;
        _transport.Start(_events);
        _log.Info($"[host] Session '{SessionName}' started on {_transport.Name}; up to {MaxPlayers} players{(RequiresPassword ? ", password protected" : string.Empty)}.");
    }

    public void Poll()
    {
        if (_state != State.Running)
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

        if (_state != State.Running)
        {
            ReleaseTransport();
            return;
        }

        var now = _clock.NowMs;
        foreach (var peer in _peers.Values.ToArray())
        {
            if (peer.Phase is Phase.AwaitingHello or Phase.AwaitingAuth && now > peer.Deadline)
            {
                Drop(peer, DisconnectReason.HandshakeTimeout, null);
            }
        }
    }

    /// <summary>Smoothed round-trip time to a joined client in milliseconds, or -1 if unknown.</summary>
    public int GetRoundTripTimeMs(PlayerId player) => FindJoined(player)?.Connection.RoundTripTimeMs ?? -1;

    /// <summary>Sends an engine payload to one joined client.</summary>
    public SendResult SendEngineData(PlayerId to, byte channel, ReadOnlySpan<byte> payload)
    {
        if (!IsEngineChannel(channel))
        {
            return SendResult.InvalidChannel;
        }

        if (_state != State.Running)
        {
            return SendResult.NotConnected;
        }

        var peer = FindJoined(to);
        if (peer is null)
        {
            return SendResult.NotConnected;
        }

        _writer.Reset();
        EngineDataMessage.WriteTo(_writer, payload);
        return peer.Connection.Send(channel, _writer.WrittenSpan);
    }

    /// <summary>Sends an engine payload to every joined client, optionally skipping one.</summary>
    public void BroadcastEngineData(byte channel, ReadOnlySpan<byte> payload, PlayerId except = default)
    {
        if (!IsEngineChannel(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }

        if (_state != State.Running)
        {
            return;
        }

        _writer.Reset();
        EngineDataMessage.WriteTo(_writer, payload);
        foreach (var peer in _peers.Values)
        {
            if (peer.Phase == Phase.Joined && peer.Player!.Id != except)
            {
                peer.Connection.Send(channel, _writer.WrittenSpan);
            }
        }
    }

    /// <summary>Sends a chat line as the host. It is also raised locally through <see cref="ChatReceived"/>.</summary>
    public void SendChat(string text)
    {
        var clean = TextRules.SanitizeChat(text);
        if (clean.Length == 0 || _state != State.Running)
        {
            return;
        }

        BroadcastChat(LocalPlayer, clean);
    }

    /// <summary>Removes a player. Returns false if no such player is connected.</summary>
    public bool Kick(PlayerId player, string? message = null)
    {
        var peer = FindJoined(player);
        if (peer is null)
        {
            return false;
        }

        _log.Info($"[host] Kicking {peer.Player}.");
        Drop(peer, DisconnectReason.Kicked, message);
        return true;
    }

    /// <summary>Ends the session for everyone and releases the transport. No events are raised afterwards.</summary>
    public void Stop()
    {
        if (_state == State.Stopped)
        {
            return;
        }

        var wasRunning = _state == State.Running;
        _state = State.Stopped;
        foreach (var peer in _peers.Values)
        {
            peer.Phase = Phase.Closed;
            peer.Connection.Disconnect(DisconnectReason.HostShutdown);
        }

        _peers.Clear();
        _players.RemoveAll(p => p.Id != PlayerId.Host);
        if (!_polling)
        {
            // Inside Poll() the transport is still running its own loop; Poll() releases it on the way out.
            ReleaseTransport();
        }

        if (wasRunning)
        {
            _log.Info($"[host] Session '{SessionName}' stopped.");
        }
    }

    public void Dispose() => Stop();

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

    private static bool IsEngineChannel(byte channel) =>
        channel is Channels.EngineReliable or Channels.EngineUnreliable or Channels.Bulk;

    private Peer? FindJoined(PlayerId id)
    {
        foreach (var peer in _peers.Values)
        {
            if (peer.Phase == Phase.Joined && peer.Player!.Id == id)
            {
                return peer;
            }
        }

        return null;
    }

    private void OnConnected(TransportConnection connection)
    {
        if (_state != State.Running)
        {
            connection.Disconnect(DisconnectReason.HostShutdown);
            return;
        }

        var now = _clock.NowMs;
        var limits = _options.ClientLimits;
        var peer = new Peer(connection)
        {
            Deadline = now + _options.HandshakeTimeoutMs,
            Messages = new TokenBucket(limits.MessagesPerSecond, limits.MessageBurst, now),
            Bytes = new TokenBucket(limits.BytesPerSecond, limits.ByteBurst, now),
            Chat = new TokenBucket(limits.ChatPerSecond, limits.ChatBurst, now),
        };
        connection.Tag = peer;
        _peers[connection.Id] = peer;
        _log.Debug($"[host] Connection from {connection}.");
    }

    private void OnDisconnected(TransportConnection connection, DisconnectReason reason, string? message)
    {
        if (connection.Tag is not Peer peer || !_peers.Remove(connection.Id))
        {
            return;
        }

        var wasJoined = peer.Phase == Phase.Joined;
        peer.Phase = Phase.Closed;
        if (!wasJoined)
        {
            _log.Debug($"[host] {connection} left during the handshake ({reason}).");
            return;
        }

        var player = peer.Player!;
        _players.Remove(player);
        _log.Info($"[host] {player.Name} ({player.Id}) left: {reason}.");

        _writer.Reset();
        new PlayerLeftMessage(player.Id, reason).WriteTo(_writer);
        SendToJoined(Channels.Control, _writer.WrittenSpan, except: null);

        Raise(() => PlayerLeft?.Invoke(player, reason), nameof(PlayerLeft));
    }

    private void OnReceived(TransportConnection connection, byte channel, ReadOnlySpan<byte> payload)
    {
        if (connection.Tag is not Peer peer || peer.Phase == Phase.Closed || peer.Closing || _state != State.Running)
        {
            return;
        }

        var now = _clock.NowMs;
        if (!peer.Messages.TryTake(1, now) || !peer.Bytes.TryTake(payload.Length, now))
        {
            _log.Warn($"[host] {Describe(peer)} exceeded its traffic budget.");
            Drop(peer, DisconnectReason.RateLimited, null);
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
            switch (peer.Phase)
            {
                case Phase.AwaitingHello:
                    Expect(type == MessageType.Hello && channel == Channels.Control, type, channel, peer);
                    HandleHello(peer, ref reader);
                    break;

                case Phase.AwaitingAuth:
                    Expect(type == MessageType.AuthResponse && channel == Channels.Control, type, channel, peer);
                    HandleAuthResponse(peer, AuthResponseMessage.ReadBody(ref reader));
                    break;

                case Phase.Joined:
                    HandleJoinedMessage(peer, type, channel, ref reader, now);
                    break;
            }
        }
        catch (ProtocolViolationException ex)
        {
            _log.Warn($"[host] Protocol violation from {Describe(peer)}: {ex.Message}");
            Drop(peer, DisconnectReason.ProtocolViolation, null);
        }
    }

    private void HandleHello(Peer peer, ref PacketReader reader)
    {
        var version = HelloMessage.ReadProtocolVersion(ref reader);
        if (version != ProtocolConstants.ProtocolVersion)
        {
            Drop(peer, DisconnectReason.ProtocolMismatch, $"Host uses Tether protocol {ProtocolConstants.ProtocolVersion} ({TetherVersion.Plugin}); you use protocol {version}.");
            return;
        }

        var hello = HelloMessage.ReadRemainder(ref reader, version);
        if (!_options.Game.IsCompatibleBuild(hello.GameBuild))
        {
            Drop(peer, DisconnectReason.GameBuildMismatch, $"Host game build: {_options.Game.GameBuild}.");
            return;
        }

        if (!_options.Game.IsCompatibleMods(hello.ModFingerprint))
        {
            Drop(peer, DisconnectReason.ModMismatch, null);
            return;
        }

        if (OccupiedSlots() >= MaxPlayers)
        {
            Drop(peer, DisconnectReason.SessionFull, null);
            return;
        }

        peer.RequestedName = TextRules.SanitizePlayerName(hello.PlayerName);
        if (!string.Equals(hello.PluginVersion, TetherVersion.Plugin, StringComparison.Ordinal))
        {
            _log.Info($"[host] {Describe(peer)} runs Tether {hello.PluginVersion} (host {TetherVersion.Plugin}); protocol matches.");
        }

        if (_passwordKey is null)
        {
            CompleteJoin(peer);
            return;
        }

        peer.Phase = Phase.AwaitingAuth;
        peer.Nonce = PasswordProof.CreateRandom(ProtocolConstants.AuthNonceSize);
        _writer.Reset();
        new AuthChallengeMessage(peer.Nonce, _passwordSalt, _options.PasswordIterations).WriteTo(_writer);
        peer.Connection.Send(Channels.Control, _writer.WrittenSpan);
    }

    private void HandleAuthResponse(Peer peer, AuthResponseMessage response)
    {
        if (!PasswordProof.Verify(_passwordKey!, peer.Nonce!, response.Mac))
        {
            _log.Info($"[host] {Describe(peer)} gave a wrong password.");
            Drop(peer, DisconnectReason.WrongPassword, null);
            return;
        }

        CompleteJoin(peer);
    }

    private void CompleteJoin(Peer peer)
    {
        var id = AllocatePlayerId();
        if (!id.IsValid)
        {
            Drop(peer, DisconnectReason.SessionFull, null);
            return;
        }

        var player = new PlayerInfo(id, UniqueName(peer.RequestedName));
        peer.Player = player;
        peer.Phase = Phase.Joined;
        peer.Nonce = null;
        _players.Add(player);
        _players.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));

        _writer.Reset();
        new WelcomeMessage(id, SessionName, (byte)MaxPlayers, _players.ToArray()).WriteTo(_writer);
        peer.Connection.Send(Channels.Control, _writer.WrittenSpan);

        _writer.Reset();
        new PlayerJoinedMessage(player).WriteTo(_writer);
        SendToJoined(Channels.Control, _writer.WrittenSpan, except: peer);

        _log.Info($"[host] {player.Name} ({player.Id}) joined from {peer.Connection.RemoteDescription}.");
        Raise(() => PlayerJoined?.Invoke(player), nameof(PlayerJoined));
    }

    private void HandleJoinedMessage(Peer peer, MessageType type, byte channel, ref PacketReader reader, long now)
    {
        switch (type)
        {
            case MessageType.EngineData when IsEngineChannel(channel):
                var from = peer.Player!.Id;
                var body = reader.ReadToEnd();
                var handler = EngineDataReceived;
                if (handler is not null)
                {
                    try
                    {
                        handler(from, channel, body);
                    }
                    catch (Exception ex)
                    {
                        // Our own bug, not the client's fault: log and carry on.
                        _log.Error($"[host] EngineDataReceived handler threw for {peer.Player}.", ex);
                    }
                }

                break;

            case MessageType.ChatSend when channel == Channels.Control:
                var chat = ChatSendMessage.ReadBody(ref reader);
                var clean = TextRules.SanitizeChat(chat.Text);
                if (clean.Length > 0 && peer.Chat.TryTake(1, now))
                {
                    BroadcastChat(peer.Player!, clean);
                }

                break;

            default:
                throw new ProtocolViolationException($"Unexpected {type} on channel {channel} after joining.");
        }
    }

    private void BroadcastChat(PlayerInfo from, string text)
    {
        _writer.Reset();
        new ChatMessage(from.Id, text).WriteTo(_writer);
        SendToJoined(Channels.Control, _writer.WrittenSpan, except: null);
        Raise(() => ChatReceived?.Invoke(from, text), nameof(ChatReceived));
    }

    private void SendToJoined(byte channel, ReadOnlySpan<byte> message, Peer? except)
    {
        foreach (var other in _peers.Values)
        {
            if (other.Phase == Phase.Joined && !ReferenceEquals(other, except))
            {
                other.Connection.Send(channel, message);
            }
        }
    }

    /// <summary>The host, everyone joined, and anyone mid-way through password auth, who has a reserved slot.</summary>
    private int OccupiedSlots()
    {
        var count = 1;
        foreach (var peer in _peers.Values)
        {
            if (peer.Phase is Phase.Joined or Phase.AwaitingAuth)
            {
                count++;
            }
        }

        return count;
    }

    private PlayerId AllocatePlayerId()
    {
        for (var value = 2; value <= byte.MaxValue; value++)
        {
            var candidate = new PlayerId((byte)value);
            if (!_players.Exists(p => p.Id == candidate))
            {
                return candidate;
            }
        }

        return PlayerId.None;
    }

    private string UniqueName(string requested)
    {
        bool Taken(string name) => _players.Exists(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!Taken(requested))
        {
            return requested;
        }

        for (var n = 2; ; n++)
        {
            var suffix = $" ({n})";
            var stem = requested.Length + suffix.Length > ProtocolConstants.MaxPlayerNameChars
                ? requested.Substring(0, ProtocolConstants.MaxPlayerNameChars - suffix.Length).TrimEnd()
                : requested;
            var candidate = stem + suffix;
            if (!Taken(candidate))
            {
                return candidate;
            }
        }
    }

    private void Drop(Peer peer, DisconnectReason reason, string? message)
    {
        if (peer.Phase == Phase.Closed || peer.Closing)
        {
            return;
        }

        // Ignore anything else it already sent. The player list and the PlayerLeft broadcast update when the
        // transport reports the disconnect, on the next Poll.
        peer.Closing = true;
        peer.Connection.Disconnect(reason, message);
    }

    private void Expect(bool condition, MessageType type, byte channel, Peer peer)
    {
        if (!condition)
        {
            throw new ProtocolViolationException($"Unexpected {type} on channel {channel} while {peer.Phase}.");
        }
    }

    private static string Describe(Peer peer) =>
        peer.Player is { } player ? $"{player.Name} ({player.Id})" : peer.Connection.ToString();

    private void Raise(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _log.Error($"[host] {name} handler threw.", ex);
        }
    }

    private sealed class Peer
    {
        public Peer(TransportConnection connection)
        {
            Connection = connection;
        }

        public TransportConnection Connection { get; }

        public Phase Phase { get; set; } = Phase.AwaitingHello;

        /// <summary>We asked the transport to disconnect it; waiting for confirmation.</summary>
        public bool Closing { get; set; }

        public long Deadline { get; set; }

        public byte[]? Nonce { get; set; }

        public string RequestedName { get; set; } = TextRules.FallbackPlayerName;

        public PlayerInfo? Player { get; set; }

        public TokenBucket Messages { get; init; } = null!;

        public TokenBucket Bytes { get; init; } = null!;

        public TokenBucket Chat { get; init; } = null!;
    }

    /// <summary>Keeps the transport callbacks off the public surface.</summary>
    private sealed class Events : ITransportEvents
    {
        private readonly HostSession _session;

        public Events(HostSession session)
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
