// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Tether.Protocol;

/// <summary>
/// Growable writer for outgoing messages. It mirrors <see cref="PacketReader"/>.
/// </summary>
/// <remarks>
/// Buffers are rented from <see cref="ArrayPool{T}"/>. Dispose the writer to return them.
/// Writers are cheap to create and are meant to be reused via <see cref="Reset"/> on hot paths.
/// </remarks>
public sealed class PacketWriter : IDisposable
{
    private byte[] _buffer;
    private int _length;

    public PacketWriter(int initialCapacity = 256)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(16, initialCapacity));
    }

    public int Length => _length;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    public void Reset() => _length = 0;

    public void WriteByte(byte value)
    {
        Ensure(1);
        _buffer[_length++] = value;
    }

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort value)
    {
        Ensure(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length), value);
        _length += 2;
    }

    public void WriteUInt32(uint value)
    {
        Ensure(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_length), value);
        _length += 4;
    }

    public void WriteUInt64(ulong value)
    {
        Ensure(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_length), value);
        _length += 8;
    }

    /// <summary>Writes an unsigned LEB128 variable-length integer.</summary>
    public void WriteVarUInt32(uint value)
    {
        Ensure(5);
        while (value >= 0x80)
        {
            _buffer[_length++] = (byte)(value | 0x80);
            value >>= 7;
        }

        _buffer[_length++] = (byte)value;
    }

    /// <summary>Writes a length-prefixed byte string. <paramref name="maxLength"/> is checked here too, so we never send what peers would reject.</summary>
    public void WriteBytes(ReadOnlySpan<byte> value, int maxLength)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException($"Value of {value.Length} bytes exceeds the limit of {maxLength}.", nameof(value));
        }

        WriteVarUInt32((uint)value.Length);
        WriteRaw(value);
    }

    /// <summary>Writes raw bytes with no length prefix.</summary>
    public void WriteRaw(ReadOnlySpan<byte> value)
    {
        Ensure(value.Length);
        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

    /// <summary>Writes a length-prefixed UTF-8 string whose encoded size must fit <paramref name="maxBytes"/>.</summary>
    public void WriteString(string value, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > maxBytes)
        {
            throw new ArgumentException($"String of {byteCount} UTF-8 bytes exceeds the limit of {maxBytes}.", nameof(value));
        }

        WriteVarUInt32((uint)byteCount);
        Ensure(byteCount);
        _length += Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_length));
    }

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = Array.Empty<byte>();
        _length = 0;
        if (buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void Ensure(int additional)
    {
        ObjectDisposedException.ThrowIf(_buffer.Length == 0, this);
        var required = _length + additional;
        if (required <= _buffer.Length)
        {
            return;
        }

        var next = ArrayPool<byte>.Shared.Rent(Math.Max(required, _buffer.Length * 2));
        _buffer.AsSpan(0, _length).CopyTo(next);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = next;
    }
}
