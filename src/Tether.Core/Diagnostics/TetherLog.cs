// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;

namespace Tether.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>Minimal logging seam. The plugin forwards to the game log and to Tether's own log file.</summary>
public interface ITetherLog
{
    void Write(LogLevel level, string message, Exception? exception = null);
}

public sealed class NullLog : ITetherLog
{
    public static readonly NullLog Instance = new();

    private NullLog()
    {
    }

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
    }
}

public static class TetherLogExtensions
{
    public static void Debug(this ITetherLog log, string message) => log.Write(LogLevel.Debug, message);

    public static void Info(this ITetherLog log, string message) => log.Write(LogLevel.Info, message);

    public static void Warn(this ITetherLog log, string message, Exception? exception = null) => log.Write(LogLevel.Warning, message, exception);

    public static void Error(this ITetherLog log, string message, Exception? exception = null) => log.Write(LogLevel.Error, message, exception);
}
