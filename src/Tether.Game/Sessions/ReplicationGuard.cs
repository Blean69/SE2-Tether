// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Threading;
using HarmonyLib;
using Tether.Diagnostics;

namespace Tether.Game.Sessions;

/// <summary>
/// Keeps a joining player's game from crashing when the host removes an object the joining game never created.
/// </summary>
/// <remarks>
/// <para>
/// In game version 4.0.0.3169, the client side of the game's network code throws an unhandled
/// <c>NoMatchingLocalPairingException</c> from <c>OutOfProcessClientPeerTransport.ProcessReplicationDestruction</c>
/// when the host destroys an object the client has no copy of. The crash handler then ends the game.
/// </para>
/// <para>
/// There is nothing to remove in that case, so Tether skips that one message and logs it. Only that exact case
/// is handled: any other exception, including the same exception type from anywhere else, is left to the game.
/// </para>
/// </remarks>
internal static class ReplicationGuard
{
    private const string TransportType = "Keen.VRage.Multiplayer.Transport.OutOfProcess.OutOfProcessPeerTransport";
    private const string PairingException = "Keen.VRage.Multiplayer.Replications.InProcess.NoMatchingLocalPairingException";
    private const string DestroyFrame = "ProcessReplicationDestruction";

    private static int s_skipped;

    public static void Install(Harmony harmony)
    {
        var transport = AccessTools.TypeByName(TransportType) ?? throw new TypeLoadException(TransportType);
        var method = AccessTools.DeclaredMethod(transport, "ProcessMessage")
            ?? throw new MissingMethodException(TransportType, "ProcessMessage");
        harmony.Patch(method, finalizer: new HarmonyMethod(typeof(ReplicationGuard), nameof(ProcessMessageFinalizer)));
    }

    /// <summary>Harmony finalizer: returning null swallows the exception, returning it rethrows.</summary>
    public static Exception? ProcessMessageFinalizer(Exception? __exception)
    {
        if (__exception is null
            || __exception.GetType().FullName != PairingException
            || __exception.StackTrace?.Contains(DestroyFrame, StringComparison.Ordinal) != true)
        {
            return __exception;
        }

        var count = Interlocked.Increment(ref s_skipped);
        if (count <= 3 || count % 100 == 0)
        {
            GameModule.Log.Warn($"Joining: the host removed an object this game never received ({__exception.Message}). " +
                $"Skipped it instead of crashing ({count} so far).");
        }

        return null;
    }
}
