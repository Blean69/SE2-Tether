// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System.Diagnostics;

namespace Tether.Diagnostics;

/// <summary>Monotonic milliseconds. Injected so session timeouts and rate limits are testable.</summary>
public interface IClock
{
    long NowMs { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    private SystemClock()
    {
    }

    // Elapsed-time form avoids the overflow that Stopwatch.GetTimestamp() * 1000 hits after long uptimes.
    private static readonly long Origin = Stopwatch.GetTimestamp();

    public long NowMs => (long)Stopwatch.GetElapsedTime(Origin).TotalMilliseconds;
}

/// <summary>Clock that only moves when told to. For tests and simulations.</summary>
public sealed class ManualClock : IClock
{
    public ManualClock(long startMs = 0)
    {
        NowMs = startMs;
    }

    public long NowMs { get; private set; }

    public void Advance(long ms) => NowMs += ms;
}
