// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Tether.Diagnostics;

namespace Tether.Runtime.Bootstrap;

/// <summary>
/// Subscribes to <c>PluginHost</c> lifecycle events by name, through reflection.
/// </summary>
/// <remarks>
/// Keen has moved this API between builds before, so Tether does not compile against the event
/// signatures. If an event is renamed or changes shape, Tether logs it and carries on instead of failing
/// to load.
/// </remarks>
internal static class HostEvents
{
    public static bool TrySubscribe(object host, string eventName, Action<object?> callback, ITetherLog log)
    {
        try
        {
            var evt = host.GetType().GetEvent(eventName, BindingFlags.Public | BindingFlags.Instance);
            var handlerType = evt?.EventHandlerType;
            var invoke = handlerType?.GetMethod("Invoke");
            if (evt is null || handlerType is null || invoke is null || invoke.ReturnType != typeof(void))
            {
                log.Warn($"PluginHost has no usable event '{eventName}' in this game build.");
                return false;
            }

            // Build (T1 a, T2 b, ...) => callback((object)a), whatever the delegate's parameters are.
            var parameters = invoke.GetParameters().Select(p => Expression.Parameter(p.ParameterType, p.Name)).ToArray();
            Expression firstArg = parameters.Length > 0
                ? Expression.Convert(parameters[0], typeof(object))
                : Expression.Constant(null, typeof(object));

            Action<object?> guarded = arg =>
            {
                try
                {
                    callback(arg);
                }
                catch (Exception ex)
                {
                    log.Error($"Tether handler for {eventName} failed.", ex);
                }
            };

            var body = Expression.Invoke(Expression.Constant(guarded), firstArg);
            var handler = Expression.Lambda(handlerType, body, parameters).Compile();
            evt.AddEventHandler(host, handler);
            log.Debug($"Subscribed to PluginHost.{eventName} ({handlerType}).");
            return true;
        }
        catch (Exception ex)
        {
            log.Warn($"Could not subscribe to PluginHost.{eventName}.", ex);
            return false;
        }
    }

    /// <summary>Logs PluginHost's public surface, for Phase 0 mapping.</summary>
    public static void Describe(object host, ITetherLog log)
    {
        try
        {
            var type = host.GetType();
            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m is EventInfo or PropertyInfo || (m is MethodInfo mi && !mi.IsSpecialName))
                .Select(m => $"{m.MemberType} {m.Name}")
                .OrderBy(s => s, StringComparer.Ordinal);
            log.Info($"PluginHost ({type.FullName}, {type.Assembly.GetName().Name}): {string.Join(", ", members)}");
        }
        catch (Exception ex)
        {
            log.Warn("Could not describe PluginHost.", ex);
        }
    }

    public static string Summarize(object? arg) => arg switch
    {
        null => "no argument",
        ICollection collection => $"{arg.GetType().Name} with {collection.Count} item(s)",
        _ => arg.GetType().FullName ?? arg.GetType().Name,
    };
}
