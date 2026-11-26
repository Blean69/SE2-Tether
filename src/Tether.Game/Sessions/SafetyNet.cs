// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Keen.VRage.DCS.Accessors;
using Keen.VRage.DCS.Scenes;
using Tether.Diagnostics;
using KTask = Keen.VRage.Library.Threading.Task;

namespace Tether.Game.Sessions;

/// <summary>
/// Keeps a game running when one piece of its own copy of the world fails, instead of letting one exception end it.
/// </summary>
/// <remarks>
/// <para>
/// Every multiplayer crash found in testing had the same shape: the game builds or updates something it received
/// from the other player, one component throws, and nothing catches it. This guards the few places all of those go
/// through, only in client scenes (the world copy a game shows its player; the host's own simulation, the server
/// scene, is left alone so a bug there can't silently damage the saved world):
/// </para>
/// <list type="bullet">
/// <item><b>Component setup</b> (<c>EntityAllocator.BuildOne</c>, its <c>RunInit</c>): a component whose setup
/// throws is logged and the object is built without the rest of that component's setup. Before, the half-built
/// object crashed the game when its queued changes were replayed.</item>
/// <item><b>Per-frame updates</b> (<c>Scene.RunJob</c>): a job that throws counts as run for that frame and is
/// logged, so the scene's frame completes normally.</item>
/// <item><b>Replication batches</b> (<c>LegacyClientSessionComponent.InitializeObjectsAsync</c>): a batch of
/// objects that fails to build is logged instead of reaching the crash handler as an unobserved failure.</item>
/// </list>
/// <para>
/// Everything caught is written to <c>tether.log</c> (rate-limited), so the causes can still be found and fixed.
/// <c>-tether:safety=off</c> turns all of this off and restores the game's own behaviour.
/// </para>
/// </remarks>
internal static class SafetyNet
{
    [ThreadStatic]
    private static bool t_buildingClientObject;

    private static readonly ConcurrentDictionary<int, int> s_jobFailures = new();
    private static int s_setupFailures;
    private static int s_batchFailures;

    // ----- Component setup ------------------------------------------------------------------------------------

    private const string Allocator = "Keen.VRage.DCS.Builders.EntityAllocator";

    public static void InstallComponentSetup(Harmony harmony)
    {
        var allocator = AccessTools.TypeByName(Allocator) ?? throw new TypeLoadException(Allocator);
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var buildOne = allocator.GetMethods(all).FirstOrDefault(m =>
                m.Name == "BuildOne" && m.GetParameters() is { Length: > 0 } p && p[0].ParameterType == typeof(DEntityContext))
            ?? throw new MissingMethodException(Allocator, "BuildOne");
        var runInit = allocator.GetMethods(all).FirstOrDefault(m => m.Name.Contains("g__RunInit", StringComparison.Ordinal))
            ?? throw new MissingMethodException(Allocator, "RunInit");

        harmony.Patch(buildOne,
            prefix: new HarmonyMethod(typeof(SafetyNet), nameof(BuildOnePrefix)),
            finalizer: new HarmonyMethod(typeof(SafetyNet), nameof(BuildOneFinalizer)));
        harmony.Patch(runInit, finalizer: new HarmonyMethod(typeof(SafetyNet), nameof(RunInitFinalizer)));
    }

    public static void BuildOnePrefix(DEntityContext dataEntity, out bool __state)
    {
        __state = t_buildingClientObject;
        t_buildingClientObject = IsClientScene(SceneOf(dataEntity));
    }

    public static Exception? BuildOneFinalizer(Exception? __exception, bool __state)
    {
        t_buildingClientObject = __state;
        return __exception;
    }

    public static Exception? RunInitFinalizer(Exception? __exception)
    {
        if (__exception is null || !t_buildingClientObject || __exception is OutOfMemoryException)
        {
            return __exception;
        }

        Report(ref s_setupFailures, "Safety net: a component failed to set up in this game's copy of an object; built the object without the rest of that setup", __exception);
        return null;
    }

    // ----- Per-frame updates ----------------------------------------------------------------------------------

    public static void InstallFrameUpdates(Harmony harmony)
    {
        var runJob = AccessTools.DeclaredMethod(typeof(Scene), "RunJob", new[] { typeof(int) })
            ?? throw new MissingMethodException(nameof(Scene), "RunJob");
        harmony.Patch(runJob, finalizer: new HarmonyMethod(typeof(SafetyNet), nameof(RunJobFinalizer)));
    }

    public static Exception? RunJobFinalizer(Exception? __exception, Scene __instance, int jobIndex, ref bool __result)
    {
        if (__exception is null)
        {
            return null;
        }

        if (__exception is OutOfMemoryException || !IsClientScene(__instance))
        {
            return __exception;
        }

        // The job did run, at least in part: let the scene finish its usual after-run bookkeeping.
        __result = true;
        var count = s_jobFailures.AddOrUpdate(jobIndex, 1, (_, c) => c + 1);
        if (count <= 2 || count % 600 == 0)
        {
            GameModule.Log.Warn($"Safety net: a per-frame update failed ({FirstFrame(__exception)}); skipped it for this frame instead of crashing ({count} time(s) for this update).",
                count == 1 ? __exception : null);
        }

        return null;
    }

    // ----- Replication batches --------------------------------------------------------------------------------

    private const string ClientReplication = "Keen.VRage.Multiplayer.Replications.InProcess.LegacyClientSessionComponent";

    public static void InstallReplicationBatches(Harmony harmony)
    {
        var component = AccessTools.TypeByName(ClientReplication) ?? throw new TypeLoadException(ClientReplication);
        var initialize = component.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.Name == "InitializeObjectsAsync" && m.GetParameters().Length == 2)
            ?? throw new MissingMethodException(ClientReplication, "InitializeObjectsAsync");
        harmony.Patch(initialize, postfix: new HarmonyMethod(typeof(SafetyNet), nameof(InitializeObjectsPostfix)));
    }

    public static void InitializeObjectsPostfix(ref KTask __result) => __result = GuardBatchAsync(__result);

    private static async KTask GuardBatchAsync(KTask batch)
    {
        try
        {
            await batch;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            Report(ref s_batchFailures, "Safety net: a batch of objects from the other player failed to build here; carried on without them", ex);
        }
    }

    // ----------------------------------------------------------------------------------------------------------

    private static Scene? SceneOf(DEntityContext data)
    {
        try
        {
            return data.Scene;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The game names the scene a player sees "Client"; the host's simulation is "Server".</summary>
    private static bool IsClientScene(Scene? scene)
    {
        try
        {
            return string.Equals(scene?.DebugName, "Client", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string FirstFrame(Exception ex)
    {
        var trace = ex.StackTrace;
        var line = trace?.Split('\n').FirstOrDefault(l => l.Contains(" at ", StringComparison.Ordinal))?.Trim();
        return $"{ex.GetType().Name} {line ?? ex.Message}";
    }

    private static void Report(ref int counter, string message, Exception ex)
    {
        var count = Interlocked.Increment(ref counter);
        if (count <= 3 || count % 100 == 0)
        {
            GameModule.Log.Warn($"{message} ({count} so far).", count <= 3 ? ex : null);
        }
    }
}
