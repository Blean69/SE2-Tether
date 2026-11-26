// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Tether.Protocol;

namespace Tether.Diagnostics;

/// <summary>
/// Measures link quality for the network tests. Each side sends small numbered probes on the unreliable
/// engine channel at a fixed rate, and the receiver counts gaps in the numbering to estimate packet loss.
/// </summary>
/// <remarks>
/// Probes are only used by the network self-test and tether-nettest, while no game-engine adapter owns
/// engine data. Their payload starts with a magic prefix so they can't be mistaken for engine data.
/// </remarks>
public sealed class LinkProbe
{
    public const int PacketSize = 7;

    private static ReadOnlySpan<byte> Magic => "TPr"u8;

    private readonly IClock _clock;
    private readonly int _intervalMs;
    private readonly Dictionary<PlayerId, Window> _windows = new();
    private long _nextSendAt;
    private uint _sequence;

    public LinkProbe(IClock clock, int intervalMs = 100)
    {
        _clock = clock;
        _intervalMs = Math.Max(10, intervalMs);
        _nextSendAt = clock.NowMs;
    }

    /// <summary>Writes the next probe into <paramref name="buffer"/> if one is due.</summary>
    public bool TryCreateProbe(Span<byte> buffer)
    {
        var now = _clock.NowMs;
        if (now < _nextSendAt || buffer.Length < PacketSize)
        {
            return false;
        }

        _nextSendAt = now + _intervalMs;
        Magic.CopyTo(buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(3), ++_sequence);
        return true;
    }

    /// <summary>Returns true if <paramref name="payload"/> was a probe, and records it.</summary>
    public bool TryReceive(PlayerId from, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != PacketSize || !payload.StartsWith(Magic))
        {
            return false;
        }

        var sequence = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(3));
        if (!_windows.TryGetValue(from, out var window))
        {
            window = new Window { LastSeen = sequence - 1 };
            _windows[from] = window;
        }

        // Unreliable-sequenced never delivers older probes, but be defensive with hostile peers.
        if (sequence > window.LastSeen)
        {
            window.Expected += sequence - window.LastSeen;
            window.Received++;
            window.LastSeen = sequence;
        }

        return true;
    }

    public void Forget(PlayerId player) => _windows.Remove(player);

    /// <summary>Probe statistics per peer since the previous call, which resets them.</summary>
    public IReadOnlyList<LinkSample> TakeSamples()
    {
        var samples = new List<LinkSample>(_windows.Count);
        foreach (var (player, window) in _windows)
        {
            samples.Add(new LinkSample(player, window.Received, window.Expected));
            window.Received = 0;
            window.Expected = 0;
        }

        return samples;
    }

    private sealed class Window
    {
        public uint LastSeen { get; set; }

        public long Received { get; set; }

        public long Expected { get; set; }
    }
}

public readonly record struct LinkSample(PlayerId Player, long Received, long Expected)
{
    public double LossPercent => Expected <= 0 ? 0 : Math.Clamp(100.0 * (Expected - Received) / Expected, 0, 100);

    public override string ToString() =>
        Expected <= 0 ? $"{Player}: no probes" : $"{Player}: {Received}/{Expected} probes, {LossPercent:0.0}% loss";
}
