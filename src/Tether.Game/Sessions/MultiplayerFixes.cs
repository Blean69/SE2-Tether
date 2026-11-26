// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Threading;
using HarmonyLib;
using Keen.VRage.Multiplayer.Replications.FailureStrategies;
using Keen.VRage.Multiplayer.Transport;
using Tether.Diagnostics;

namespace Tether.Game.Sessions;

/// <summary>
/// Narrow guards for crashes found in the game's own multiplayer code (game version 4.0.0.3169) while testing.
/// </summary>
/// <remarks>
/// Each guard handles one observed failure and leaves everything else to the game. Each is installed separately,
/// so a game update that breaks one doesn't take the others down.
/// </remarks>
internal static class MultiplayerFixes
{
    private static int s_wheelAudio;
    private static int s_remoteFaults;

    // ----- Wheel sounds ---------------------------------------------------------------------------------------

    private const string WheelAudioAdapter =
        "Keen.Game2.Client.WorldObjects.CubeBlocks.MechanicalBlocks.Wheels.DrivingWheelAudioComponent+AudioAdapter";

    /// <summary>
    /// A joining player's game crashed when a wheel touched a surface whose physics material it couldn't find:
    /// <c>AudioAdapter.TryGetEffectDefinition</c> passes the missing definition on as a dictionary key, which throws
    /// <see cref="ArgumentNullException"/>. Tether reports "no rolling sound for this surface" instead.
    /// </summary>
    public static void InstallWheelAudio(Harmony harmony)
    {
        var adapter = AccessTools.TypeByName(WheelAudioAdapter) ?? throw new TypeLoadException(WheelAudioAdapter);
        var method = AccessTools.DeclaredMethod(adapter, "TryGetEffectDefinition")
            ?? throw new MissingMethodException(WheelAudioAdapter, "TryGetEffectDefinition");
        harmony.Patch(method, finalizer: new HarmonyMethod(typeof(MultiplayerFixes), nameof(WheelAudioFinalizer)));
    }

    public static Exception? WheelAudioFinalizer(Exception? __exception, ref bool __result)
    {
        if (__exception is not ArgumentNullException)
        {
            return __exception;
        }

        __result = false;
        Report(ref s_wheelAudio, "Wheel sound: a wheel touched a surface whose material this game doesn't know; played no rolling sound instead of crashing");
        return null;
    }

    // ----- Outgoing updates -----------------------------------------------------------------------------------

    private const string Transport = "Keen.VRage.Multiplayer.Transport.OutOfProcess.OutOfProcessPeerTransport";
    private static AccessTools.FieldRef<object, IReplicationSerializationContext?>? s_context;

    /// <summary>
    /// A host's game crashed while sending updates: one entity in the batch had no network id for that player yet,
    /// and the default failure strategy (<see cref="ReplicationFailureStrategy.Throw"/>) throws while serializing.
    /// Tether puts the game's own tolerant strategy in force while a batch is serialized, so such an entity goes out
    /// as "unknown" and the receiving game skips that one update, as it already does for its own misses.
    /// </summary>
    public static void InstallTolerantDispatch(Harmony harmony)
    {
        var transport = AccessTools.TypeByName(Transport) ?? throw new TypeLoadException(Transport);
        s_context = AccessTools.FieldRefAccess<IReplicationSerializationContext?>(transport, "_context");
        var method = AccessTools.DeclaredMethod(transport, "DispatchMessages") ?? throw new MissingMethodException(Transport, "DispatchMessages");
        harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(MultiplayerFixes), nameof(DispatchPrefix)),
            finalizer: new HarmonyMethod(typeof(MultiplayerFixes), nameof(DispatchFinalizer)));
    }

    public static void DispatchPrefix(object __instance, out IReplicationSerializationContext.StrategyToken __state)
    {
        var context = s_context!(__instance);
        __state = context is null ? default : context.PushStrategy(ReplicationFailureStrategy.NullOnAnyFail);
    }

    public static Exception? DispatchFinalizer(Exception? __exception, IReplicationSerializationContext.StrategyToken __state)
    {
        __state.Dispose();
        return __exception;
    }

    // ----- Failures reported by the other player --------------------------------------------------------------

    private const string FaultDescriptor = "Keen.VRage.Multiplayer.Transport.StoryFaultDescriptor";
    private const string RemoteFault = "Keen.VRage.Multiplayer.NetworkStories.NetworkStoryRemoteFaultException";
    private const string SkipStory = "Keen.VRage.Multiplayer.NetworkStories.SkipNetworkStoryException";
    private static Type? s_skipType;

    /// <summary>
    /// A joining player's game crashed because a request it sent failed on the host (the host's game logged a
    /// <see cref="NullReferenceException"/> in <c>ProjectileWeaponBaseComponent.Detach</c> and carried on). The
    /// failure comes back as a <c>NetworkStoryRemoteFaultException</c>, which nothing on the receiving side catches.
    /// Tether turns it into the game's own "skip this story" cancellation, which the game handles, and logs it.
    /// </summary>
    public static void InstallRemoteFaults(Harmony harmony)
    {
        var descriptor = AccessTools.TypeByName(FaultDescriptor) ?? throw new TypeLoadException(FaultDescriptor);
        s_skipType = AccessTools.TypeByName(SkipStory) ?? throw new TypeLoadException(SkipStory);
        var method = AccessTools.DeclaredMethod(descriptor, "ToException") ?? throw new MissingMethodException(FaultDescriptor, "ToException");
        harmony.Patch(method, postfix: new HarmonyMethod(typeof(MultiplayerFixes), nameof(RemoteFaultPostfix)));
    }

    public static void RemoteFaultPostfix(ref Exception __result)
    {
        if (__result?.GetType().FullName != RemoteFault)
        {
            return;
        }

        Report(ref s_remoteFaults, $"Network: the other player's game failed to carry out an action ({__result.Message}); cancelled it here instead of crashing");
        __result = (Exception)Activator.CreateInstance(s_skipType!, nonPublic: true)!;
    }

    // ----------------------------------------------------------------------------------------------------------

    private static void Report(ref int counter, string message)
    {
        var count = Interlocked.Increment(ref counter);
        if (count <= 3 || count % 100 == 0)
        {
            GameModule.Log.Warn($"{message} ({count} so far).");
        }
    }
}
