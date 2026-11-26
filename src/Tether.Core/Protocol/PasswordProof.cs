// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Security.Cryptography;
using System.Text;

namespace Tether.Protocol;

/// <summary>
/// Challenge-response proof of a shared session password, so the password itself never crosses the network.
/// </summary>
/// <remarks>
/// <para>key = PBKDF2-SHA256(password, salt, iterations); mac = HMAC-SHA256(key, context || nonce).</para>
/// <para>
/// This keeps strangers out of a co-op session and keeps the plaintext password off the wire. It does
/// <b>not</b> encrypt the session. Someone who records a challenge and its response can still mount an
/// offline guessing attack, which PBKDF2 only slows down. Steam P2P connections are encrypted by Steam.
/// The direct UDP transport is for LAN and trusted use until session encryption lands (see docs/PLAN.md §7.2).
/// </para>
/// </remarks>
public static class PasswordProof
{
    private static readonly byte[] Context = Encoding.ASCII.GetBytes("tether-auth-v1");

    public static byte[] CreateRandom(int size) => RandomNumberGenerator.GetBytes(size);

    public static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt, int iterations)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (iterations <= 0 || iterations > ProtocolConstants.MaxPasswordIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
    }

    public static byte[] ComputeMac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce)
    {
        Span<byte> message = stackalloc byte[Context.Length + nonce.Length];
        Context.CopyTo(message);
        nonce.CopyTo(message.Slice(Context.Length));
        return HMACSHA256.HashData(key, message);
    }

    public static bool Verify(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> mac)
    {
        var expected = ComputeMac(key, nonce);
        return mac.Length == expected.Length && CryptographicOperations.FixedTimeEquals(expected, mac);
    }
}
