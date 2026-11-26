// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using Tether.Protocol;
using Xunit;

namespace Tether.Tests.Protocol;

public class PacketCodecTests
{
    [Fact]
    public void Primitives_RoundTrip()
    {
        using var writer = new PacketWriter(4);
        writer.WriteByte(0xAB);
        writer.WriteBool(true);
        writer.WriteBool(false);
        writer.WriteUInt16(0xBEEF);
        writer.WriteUInt32(0xDEADBEEF);
        writer.WriteUInt64(0x0123456789ABCDEF);
        writer.WriteString("héllo 🚀", 64);
        writer.WriteBytes(new byte[] { 1, 2, 3 }, 8);

        var reader = new PacketReader(writer.WrittenSpan);
        Assert.Equal(0xAB, reader.ReadByte());
        Assert.True(reader.ReadBool());
        Assert.False(reader.ReadBool());
        Assert.Equal(0xBEEF, reader.ReadUInt16());
        Assert.Equal(0xDEADBEEFu, reader.ReadUInt32());
        Assert.Equal(0x0123456789ABCDEFul, reader.ReadUInt64());
        Assert.Equal("héllo 🚀", reader.ReadString(64));
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadBytes(8).ToArray());
        reader.EnsureEnd();
    }

    [Theory]
    [InlineData(0u, 1)]
    [InlineData(127u, 1)]
    [InlineData(128u, 2)]
    [InlineData(16_383u, 2)]
    [InlineData(16_384u, 3)]
    [InlineData(uint.MaxValue, 5)]
    public void VarUInt32_RoundTripsWithExpectedSize(uint value, int expectedBytes)
    {
        using var writer = new PacketWriter();
        writer.WriteVarUInt32(value);
        Assert.Equal(expectedBytes, writer.Length);

        var reader = new PacketReader(writer.WrittenSpan);
        Assert.Equal(value, reader.ReadVarUInt32());
        reader.EnsureEnd();
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x10 })] // 5th byte overflows 32 bits
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 })] // too long
    [InlineData(new byte[] { 0x80 })] // truncated
    public void VarUInt32_RejectsMalformed(byte[] data)
    {
        Assert.Throws<ProtocolViolationException>(() =>
        {
            var reader = new PacketReader(data);
            reader.ReadVarUInt32();
        });
    }

    [Fact]
    public void Reads_PastEnd_Throw()
    {
        Assert.Throws<ProtocolViolationException>(() => new PacketReader(new byte[] { 1 }).ReadUInt16());
        Assert.Throws<ProtocolViolationException>(() => new PacketReader(new byte[] { 1, 2, 3 }).ReadUInt32());
        Assert.Throws<ProtocolViolationException>(() => new PacketReader(Array.Empty<byte>()).ReadByte());
    }

    [Fact]
    public void LengthPrefix_AboveLimit_IsRejectedBeforeAllocating()
    {
        using var writer = new PacketWriter();
        writer.WriteVarUInt32(int.MaxValue); // claims ~2 GB follow
        Assert.Throws<ProtocolViolationException>(() =>
        {
            var reader = new PacketReader(writer.WrittenSpan);
            reader.ReadBytes(1024);
        });
    }

    [Fact]
    public void LengthPrefix_WithinLimitButBeyondData_Throws()
    {
        using var writer = new PacketWriter();
        writer.WriteVarUInt32(10);
        writer.WriteRaw(new byte[] { 1, 2, 3 });
        Assert.Throws<ProtocolViolationException>(() =>
        {
            var reader = new PacketReader(writer.WrittenSpan);
            reader.ReadBytes(100);
        });
    }

    [Fact]
    public void InvalidUtf8_IsRejected()
    {
        using var writer = new PacketWriter();
        writer.WriteBytes(new byte[] { 0xC3, 0x28 }, 10);
        Assert.Throws<ProtocolViolationException>(() =>
        {
            var reader = new PacketReader(writer.WrittenSpan);
            reader.ReadString(10);
        });
    }

    [Fact]
    public void InvalidBool_IsRejected()
    {
        Assert.Throws<ProtocolViolationException>(() => new PacketReader(new byte[] { 2 }).ReadBool());
    }

    [Fact]
    public void TrailingBytes_FailEnsureEnd()
    {
        Assert.Throws<ProtocolViolationException>(() =>
        {
            var reader = new PacketReader(new byte[] { 1, 2 });
            reader.ReadByte();
            reader.EnsureEnd();
        });
    }

    [Fact]
    public void Writer_RefusesToEncodeOverLimit()
    {
        using var writer = new PacketWriter();
        Assert.Throws<ArgumentException>(() => writer.WriteString(new string('x', 11), 10));
        Assert.Throws<ArgumentException>(() => writer.WriteBytes(new byte[11], 10));
    }

    [Fact]
    public void Writer_GrowsAndResets()
    {
        using var writer = new PacketWriter(16);
        var big = new byte[100_000];
        new Random(5).NextBytes(big);
        writer.WriteRaw(big);
        Assert.Equal(big, writer.WrittenSpan.ToArray());

        writer.Reset();
        Assert.Equal(0, writer.Length);
        writer.WriteByte(7);
        Assert.Equal(new byte[] { 7 }, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Writer_AfterDispose_Throws()
    {
        var writer = new PacketWriter();
        writer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => writer.WriteByte(1));
    }
}
