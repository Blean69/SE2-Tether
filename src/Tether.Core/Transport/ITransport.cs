// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using Tether.Protocol;

namespace Tether.Transport;

/// <summary>
/// A network backend, such as loopback, direct UDP, or Steam P2P. It is either a host (accepting
/// connections) or a client (one outgoing connection), depending on how it was constructed.
/// </summary>
/// <remarks>
/// <para><b>Threading:</b> transports are driven from one thread, normally the game's main thread.</para>
/// <para>
/// Every <see cref="ITransportEvents"/> callback is raised only from inside <see cref="Poll"/>, never
/// re-entrantly from <see cref="TransportConnection.Send"/> or <see cref="TransportConnection.Disconnect"/>.
/// </para>
/// </remarks>
public interface ITransport : IDisposable
{
    /// <summary>Short backend name for logs, e.g. "loopback", "udp", "steam".</summary>
    string Name { get; }

    /// <summary>Starts listening (host) or connecting (client). Events go to <paramref name="events"/>.</summary>
    void Start(ITransportEvents events);

    /// <summary>Pumps network events. Call once per frame.</summary>
    void Poll();
}

/// <summary>Receives transport events. Implemented by the session layer.</summary>
public interface ITransportEvents
{
    void OnConnected(TransportConnection connection);

    /// <summary>
    /// Raised exactly once per connection, whichever side ended it. This includes connections that
    /// never got established, which end with <see cref="DisconnectReason.ConnectionFailed"/>.
    /// </summary>
    /// <param name="message">Optional text sent by the remote side with its reason. Untrusted.</param>
    void OnDisconnected(TransportConnection connection, DisconnectReason reason, string? message);

    /// <summary>
    /// A complete message arrived. <paramref name="payload"/> is only valid during the call.
    /// </summary>
    void OnReceived(TransportConnection connection, byte channel, ReadOnlySpan<byte> payload);
}

/// <summary>Outcome of a send attempt.</summary>
public enum SendResult
{
    Sent,
    NotConnected,
    TooLarge,
    InvalidChannel,
    Failed,
}

/// <summary>One live link to a remote peer.</summary>
public abstract class TransportConnection
{
    private static long s_nextId;

    protected TransportConnection()
    {
        Id = System.Threading.Interlocked.Increment(ref s_nextId);
    }

    /// <summary>Process-unique id, for logs and dictionary keys.</summary>
    public long Id { get; }

    /// <summary>Human-readable remote address for logs, e.g. "192.168.1.20:27016" or "steam:7656...". Never shown to other players.</summary>
    public abstract string RemoteDescription { get; }

    public abstract bool IsConnected { get; }

    /// <summary>Smoothed round-trip time in milliseconds, or -1 if unknown.</summary>
    public abstract int RoundTripTimeMs { get; }

    /// <summary>Largest payload <see cref="Send"/> accepts on <see cref="Delivery.UnreliableSequenced"/> channels.</summary>
    public abstract int MaxUnreliablePayload { get; }

    /// <summary>Free slot for the session layer to attach its per-connection state.</summary>
    public object? Tag { get; set; }

    /// <summary>Queues <paramref name="payload"/> for delivery. The payload is copied before this returns.</summary>
    public abstract SendResult Send(byte channel, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Closes the connection gracefully and tells the remote side why, where the backend supports it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reliable messages already sent are delivered before the remote side sees the disconnect. This is a
    /// best effort with a short, bounded wait.
    /// </para>
    /// <para>
    /// No more messages can be sent on the connection. The local <see cref="ITransportEvents.OnDisconnected"/>
    /// follows during the next <see cref="ITransport.Poll"/>. Repeated calls do nothing.
    /// </para>
    /// </remarks>
    public abstract void Disconnect(DisconnectReason reason, string? message = null);

    public override string ToString() => $"{RemoteDescription} (conn {Id})";
}
