// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Buffers.Binary;
using System.Text;

namespace Tether.Protocol;

/// <summary>
/// Bounds-checked reader over untrusted bytes.
/// </summary>
/// <remarks>
/// Every read validates the remaining length first. Variable-length values always take a caller-supplied
/// maximum, so a hostile length prefix can never cause a large allocation. Any malformed input throws
/// <see cref="ProtocolViolationException"/>, and nothing else.
/// </remarks>
public ref struct PacketReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public PacketReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public readonly int Position => _position;

    public readonly int Remaining => _data.Length - _position;

    public byte ReadByte()
    {
        Require(1);
        return _data[_position++];
    }

    public bool ReadBool()
    {
        var value = ReadByte();
        return value switch
        {
            0 => false,
            1 => true,
            _ => throw new ProtocolViolationException($"Invalid boolean value {value}."),
        };
    }

    public ushort ReadUInt16()
    {
        Require(2);
        var value = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_position, 2));
        _position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        Require(4);
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return value;
    }

    public ulong ReadUInt64()
    {
        Require(8);
        var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_position, 8));
        _position += 8;
        return value;
    }

    /// <summary>Reads an unsigned LEB128 variable-length integer of at most 32 bits.</summary>
    public uint ReadVarUInt32()
    {
        uint result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = ReadByte();
            if (shift == 28 && (b & 0xF0) != 0)
            {
                throw new ProtocolViolationException("Variable-length integer overflows 32 bits.");
            }

            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw new ProtocolViolationException("Variable-length integer is too long.");
    }

    /// <summary>Reads a length prefix and checks it against <paramref name="max"/> and the remaining bytes.</summary>
    public int ReadLength(int max)
    {
        var length = ReadVarUInt32();
        if (length > (uint)max)
        {
            throw new ProtocolViolationException($"Length {length} exceeds the limit of {max}.");
        }

        return (int)length;
    }

    /// <summary>Reads a length-prefixed byte string of at most <paramref name="maxLength"/> bytes, without copying.</summary>
    public ReadOnlySpan<byte> ReadBytes(int maxLength)
    {
        var length = ReadLength(maxLength);
        return ReadRaw(length);
    }

    /// <summary>Reads exactly <paramref name="count"/> raw bytes, without copying.</summary>
    public ReadOnlySpan<byte> ReadRaw(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Require(count);
        var slice = _data.Slice(_position, count);
        _position += count;
        return slice;
    }

    /// <summary>Reads all remaining bytes, without copying.</summary>
    public ReadOnlySpan<byte> ReadToEnd()
    {
        var slice = _data.Slice(_position);
        _position = _data.Length;
        return slice;
    }

    /// <summary>
    /// Reads a length-prefixed UTF-8 string. <paramref name="maxBytes"/> limits the encoded size.
    /// Invalid UTF-8 is a protocol violation.
    /// </summary>
    public string ReadString(int maxBytes)
    {
        var bytes = ReadBytes(maxBytes);
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ProtocolViolationException("String is not valid UTF-8.");
        }
    }

    /// <summary>Throws if any bytes are left, because trailing garbage means a malformed or mismatched message.</summary>
    public readonly void EnsureEnd()
    {
        if (_position != _data.Length)
        {
            throw new ProtocolViolationException($"{_data.Length - _position} unexpected trailing byte(s).");
        }
    }

    private readonly void Require(int count)
    {
        if (_data.Length - _position < count)
        {
            throw new ProtocolViolationException("Message ended unexpectedly.");
        }
    }
}
