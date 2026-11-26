// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Globalization;

namespace Tether.Transport.Udp;

/// <summary>A host name or address plus port, as typed by a player: "example.org", "192.168.1.20:27027", "[::1]:27027".</summary>
public readonly record struct UdpEndpoint(string Host, int Port)
{
    public static bool TryParse(string? text, int defaultPort, out UdpEndpoint endpoint)
    {
        endpoint = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        string host;
        var port = defaultPort;
        if (text.StartsWith('['))
        {
            // [IPv6]:port or [IPv6]
            var close = text.IndexOf(']');
            if (close < 0)
            {
                return false;
            }

            host = text.Substring(1, close - 1);
            var rest = text.Substring(close + 1);
            if (rest.Length > 0 && (!rest.StartsWith(':') || !TryPort(rest.Substring(1), out port)))
            {
                return false;
            }
        }
        else if (text.AsSpan().Count(':') == 1)
        {
            var colon = text.IndexOf(':');
            host = text.Substring(0, colon);
            if (!TryPort(text.Substring(colon + 1), out port))
            {
                return false;
            }
        }
        else
        {
            // A bare name, an IPv4 address, or a bare IPv6 address (several colons, no port).
            host = text;
        }

        if (host.Length == 0 || host.Length > 253 || port is < 1 or > 65535)
        {
            return false;
        }

        endpoint = new UdpEndpoint(host, port);
        return true;
    }

    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";

    private static bool TryPort(string text, out int port) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;
}
