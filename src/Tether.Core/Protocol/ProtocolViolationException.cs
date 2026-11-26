// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;

namespace Tether.Protocol;

/// <summary>
/// Thrown when bytes received from the network do not form a valid message.
/// Anything that arrives over the network is untrusted, so decoders throw this instead of
/// crashing, and the session layer disconnects the peer that sent it.
/// </summary>
public sealed class ProtocolViolationException : Exception
{
    public ProtocolViolationException(string message)
        : base(message)
    {
    }
}
