// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;

namespace Tether.Session;

/// <summary>Classic token bucket: allows bursts up to <c>capacity</c>, refilling at <c>ratePerSecond</c>.</summary>
internal sealed class TokenBucket
{
    private readonly double _ratePerMs;
    private readonly double _capacity;
    private double _tokens;
    private long _lastMs;

    public TokenBucket(double ratePerSecond, double capacity, long nowMs)
    {
        _ratePerMs = ratePerSecond / 1000.0;
        _capacity = capacity;
        _tokens = capacity;
        _lastMs = nowMs;
    }

    public bool TryTake(double amount, long nowMs)
    {
        if (nowMs > _lastMs)
        {
            _tokens = Math.Min(_capacity, _tokens + ((nowMs - _lastMs) * _ratePerMs));
            _lastMs = nowMs;
        }

        if (_tokens < amount)
        {
            return false;
        }

        _tokens -= amount;
        return true;
    }
}
