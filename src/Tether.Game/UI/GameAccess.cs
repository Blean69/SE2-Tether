// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using Tether.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Core;
using Keen.VRage.Library.Utils;

namespace Tether.Game.UI;

/// <summary>Reaches game services that have no public accessor, by name, failing soft (null) if they move.</summary>
internal static class GameAccess
{
    /// <summary>The game's shared UI, used to open screens. Null before the game app exists.</summary>
    public static SharedUIComponent? SharedUI()
    {
        try
        {
            var engine = Singleton<VRageCore>.Instance?.Engine;
            var gameAppType = AccessTools.TypeByName("Keen.Game2.GameAppComponent");
            if (engine is null || gameAppType is null)
            {
                return null;
            }

            var gameApp = GenericGet(engine.GetType())?.MakeGenericMethod(gameAppType).Invoke(engine, new object[] { default(StringId) });
            return gameApp is null ? null : AccessTools.Method(gameAppType, "GetSharedUI")?.Invoke(gameApp, null) as SharedUIComponent;
        }
        catch (Exception ex)
        {
            GameModule.Log.Debug($"UI: could not reach the game's shared UI: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads a private field, or null if it doesn't exist in this game build.</summary>
    public static T? Field<T>(object instance, string name)
        where T : class =>
        AccessTools.Field(instance.GetType(), name)?.GetValue(instance) as T;

    // The engine entity has a generic T Get<T>(StringId) next to a non-generic Get(StringId).
    private static MethodInfo? GenericGet(Type entityType) =>
        AccessTools.GetDeclaredMethods(entityType).Concat(entityType.GetMethods())
            .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 1);
}
