// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using Keen.VRage.Core.Services.Data;
using Keen.VRage.Core.Services.Networking;
using Steamworks;
using Tether.Diagnostics;

namespace Tether.Game.Sessions;

/// <summary>
/// Lets the game send network messages larger than Steam's 512 KB limit, by splitting and reassembling them.
/// </summary>
/// <remarks>
/// <para>
/// When a player joins, the host's game sends the whole world (tens of thousands of entities) in replication
/// messages that are far larger than the 512 KB Steam allows for one message. In game version 4.0.0.3169,
/// <c>SteamPeerToPeerSession.SendMessage</c> passes each message to Steam whole; Steam refuses the oversized ones
/// (<c>k_EResultLimitExceeded</c>) and the game only counts them as dropped. The joining game then never receives
/// the world's entities, and every later update about them fails to resolve.
/// </para>
/// <para>
/// Tether splits a reliable message above <see cref="SplitAbove"/> bytes into pieces of at most
/// <see cref="PieceSize"/> bytes, each with a small header, and sends them in order on the same channel. Steam
/// delivers reliable messages on a channel in order, so the receiving side appends pieces until the message is
/// complete and only then hands the game one ordinary message with the original bytes. Smaller messages are
/// untouched. Both players need Tether for large messages to get through, which they need anyway.
/// </para>
/// <para>
/// Sending many pieces at once also needs Steam's send buffer (512 KB by default) to be larger, so Tether raises
/// the send and receive buffers once Steam is running.
/// </para>
/// </remarks>
internal static class LargeMessages
{
    /// <summary>Steam's largest single message is 512 KB; split comfortably below it.</summary>
    public const int SplitAbove = 448 * 1024;

    /// <summary>Payload bytes per piece.</summary>
    public const int PieceSize = 256 * 1024;

    private const string SessionType = "Keen.VRage.Steam.Networking.SteamPeerToPeerSession";
    private const int BufferBytes = 128 * 1024 * 1024;
    private const int BufferMessages = 50_000;

    // "TETHFRG1": marks a piece. Game messages start with the game's own serialization header instead.
    private static readonly byte[] s_magic = { 0x54, 0x45, 0x54, 0x48, 0x46, 0x52, 0x47, 0x31 };

    // Magic, message id, total length, offset of this piece.
    private const int HeaderSize = 8 + 4 + 4 + 4;

    private static readonly object s_partialLock = new();
    private static readonly Dictionary<(GameServiceUser User, byte Channel), PartialMessage> s_partial = new();
    private static readonly ConcurrentDictionary<nint, GCHandle> s_ourMessages = new();

    private static ConstructorInfo? s_messageCtor;
    private static AccessTools.FieldRef<object, bool>? s_isServer;
    private static int s_nextMessageId;
    private static int s_buffersRaised;
    private static int s_logged;

    public static void Install(Harmony harmony)
    {
        var session = AccessTools.TypeByName(SessionType) ?? throw new TypeLoadException(SessionType);
        s_isServer = AccessTools.FieldRefAccess<bool>(session, "_isServer");
        s_messageCtor = AccessTools.Constructor(typeof(IPeerToPeerSession.Message), new[]
        {
            typeof(GameServiceUser), typeof(nint), typeof(int), typeof(nint), typeof(MessageReliability), typeof(IPeerToPeerSession),
        }) ?? throw new MissingMethodException(nameof(IPeerToPeerSession.Message), ".ctor");

        harmony.Patch(Method(session, "SendMessage"), prefix: new HarmonyMethod(typeof(LargeMessages), nameof(SendPrefix)));
        harmony.Patch(Method(session, "TryReadNextMessage"), postfix: new HarmonyMethod(typeof(LargeMessages), nameof(ReadPostfix)));
        harmony.Patch(Method(session, "DisposeMessage"), prefix: new HarmonyMethod(typeof(LargeMessages), nameof(DisposePrefix)));
        harmony.Patch(Method(session, "AcceptConnection"), prefix: new HarmonyMethod(typeof(LargeMessages), nameof(AcceptPrefix)));
    }

    private static MethodInfo Method(Type type, string name) =>
        AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);

    // ----- Sending ------------------------------------------------------------------------------------------

    public static bool SendPrefix(object __instance, GameServiceUser user, ReadOnlySpan<byte> data, byte channel,
        MessageReliability reliability, ref bool __result)
    {
        RaiseBuffers(__instance);
        if (data.Length <= SplitAbove || reliability != MessageReliability.Reliable)
        {
            return true;
        }

        var session = (IPeerToPeerSession)__instance;
        var id = Interlocked.Increment(ref s_nextMessageId);
        var piece = new byte[HeaderSize + PieceSize];
        var pieces = 0;
        var ok = true;
        for (var offset = 0; offset < data.Length; offset += PieceSize)
        {
            var length = Math.Min(PieceSize, data.Length - offset);
            s_magic.CopyTo(piece, 0);
            BinaryPrimitives.WriteInt32LittleEndian(piece.AsSpan(8), id);
            BinaryPrimitives.WriteInt32LittleEndian(piece.AsSpan(12), data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(piece.AsSpan(16), offset);
            data.Slice(offset, length).CopyTo(piece.AsSpan(HeaderSize));

            // Goes through this prefix again, but a piece is below the limit, so the game's code sends it.
            ok &= session.SendMessage(user, piece.AsSpan(0, HeaderSize + length), channel, reliability);
            pieces++;
        }

        if (!ok)
        {
            GameModule.Log.Warn($"Network: Steam refused part of a {Size(data.Length)} message sent in {pieces} pieces; the other player may not receive it.");
        }
        else if (Interlocked.Increment(ref s_logged) <= 20)
        {
            GameModule.Log.Info($"Network: sent a {Size(data.Length)} message in {pieces} pieces (Steam allows 512 KB per message).");
        }

        __result = ok;
        return false;
    }

    // ----- Receiving ----------------------------------------------------------------------------------------

    public static void ReadPostfix(object __instance, byte channel, ref IPeerToPeerSession.Message message, ref bool __result)
    {
        if (!__result)
        {
            return;
        }

        RaiseBuffers(__instance);
        var contents = message.GetContents();
        if (contents.Length < HeaderSize || !contents[..8].SequenceEqual(s_magic))
        {
            return;
        }

        var user = message.RemotePeer;
        var reliability = message.Reliability;
        var id = BinaryPrimitives.ReadInt32LittleEndian(contents[8..]);
        var total = BinaryPrimitives.ReadInt32LittleEndian(contents[12..]);
        var offset = BinaryPrimitives.ReadInt32LittleEndian(contents[16..]);
        byte[]? complete = null;

        lock (s_partialLock)
        {
            var key = (user, channel);
            if (offset == 0)
            {
                if (s_partial.ContainsKey(key))
                {
                    GameModule.Log.Warn("Network: a new large message started before the previous one finished; dropped the unfinished one.");
                }

                s_partial[key] = new PartialMessage(id, new byte[total]);
            }

            if (s_partial.TryGetValue(key, out var partial) && partial.Id == id && partial.Received == offset
                && offset + contents.Length - HeaderSize <= partial.Data.Length)
            {
                contents[HeaderSize..].CopyTo(partial.Data.AsSpan(offset));
                partial.Received += contents.Length - HeaderSize;
                partial.Pieces++;
                if (partial.Received == partial.Data.Length)
                {
                    complete = partial.Data;
                    s_partial.Remove(key);
                    if (Interlocked.Increment(ref s_logged) <= 20)
                    {
                        GameModule.Log.Info($"Network: received a {Size(complete.Length)} message in {partial.Pieces} pieces.");
                    }
                }
            }
            else
            {
                s_partial.Remove(key);
                GameModule.Log.Warn($"Network: a piece of a large message arrived out of order (message {id}, offset {offset}); dropped that message.");
            }
        }

        // The piece itself is done with: give it back to Steam.
        message.Dispose();

        if (complete is not null)
        {
            message = Wrap((IPeerToPeerSession)__instance, user, complete, reliability);
            return;
        }

        // Not complete yet: read on. The nested call runs this postfix too, so it returns the next whole message,
        // or nothing if the rest hasn't arrived; the game reads again next frame.
        __result = ((IPeerToPeerSession)__instance).TryReadNextMessage(channel, out message);
    }

    private static IPeerToPeerSession.Message Wrap(IPeerToPeerSession session, GameServiceUser user, byte[] data, MessageReliability reliability)
    {
        // The game reads messages through a pointer and gives the handle back to the session when done; pin the
        // array so the pointer stays valid, and recognise the handle in DisposePrefix.
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        var handle = GCHandle.ToIntPtr(pin);
        s_ourMessages[handle] = pin;
        return (IPeerToPeerSession.Message)s_messageCtor!.Invoke(new object[]
        {
            user, pin.AddrOfPinnedObject(), data.Length, handle, reliability, session,
        });
    }

    /// <summary>Frees a reassembled message; any other handle belongs to Steam and goes to the game's code.</summary>
    public static bool DisposePrefix(nint messageHandle)
    {
        if (!s_ourMessages.TryRemove(messageHandle, out var pin))
        {
            return true;
        }

        pin.Free();
        return false;
    }

    public static void AcceptPrefix(object __instance) => RaiseBuffers(__instance);

    // ----- Steam buffers ------------------------------------------------------------------------------------

    private static void RaiseBuffers(object session)
    {
        if (Interlocked.Exchange(ref s_buffersRaised, 1) != 0)
        {
            return;
        }

        try
        {
            var isServer = s_isServer!(session);
            var ok = SetGlobal(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_SendBufferSize, BufferBytes, isServer)
                & SetGlobal(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferSize, BufferBytes, isServer)
                & SetGlobal(ESteamNetworkingConfigValue.k_ESteamNetworkingConfig_RecvBufferMessages, BufferMessages, isServer);
            if (ok)
            {
                GameModule.Log.Info($"Network: raised Steam's send and receive buffers to {Size(BufferBytes)} for large world transfers.");
            }
            else
            {
                GameModule.Log.Warn("Network: Steam didn't accept larger send and receive buffers; large world transfers may stall.");
            }
        }
        catch (Exception ex)
        {
            GameModule.Log.Warn("Network: could not raise Steam's send and receive buffers.", ex);
        }
    }

    private static bool SetGlobal(ESteamNetworkingConfigValue value, int setting, bool isServer)
    {
        var arg = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(arg, setting);
            return isServer
                ? SteamGameServerNetworkingUtils.SetConfigValue(value, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, arg)
                : SteamNetworkingUtils.SetConfigValue(value, ESteamNetworkingConfigScope.k_ESteamNetworkingConfig_Global, IntPtr.Zero, ESteamNetworkingConfigDataType.k_ESteamNetworkingConfig_Int32, arg);
        }
        finally
        {
            Marshal.FreeHGlobal(arg);
        }
    }

    private static string Size(int bytes) => bytes >= 1024 * 1024
        ? $"{bytes / (1024.0 * 1024.0):0.0} MB"
        : $"{bytes / 1024.0:0} KB";

    private sealed class PartialMessage(int id, byte[] data)
    {
        public int Id { get; } = id;
        public byte[] Data { get; } = data;
        public int Received { get; set; }
        public int Pieces { get; set; }
    }
}
