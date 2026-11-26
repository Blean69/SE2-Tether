// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using Tether.Diagnostics;

namespace Tether.Runtime.Diagnostics;

/// <summary>
/// Writes to Tether's own log file (tether.log in the data folder; the previous run is kept as
/// tether.prev.log), and mirrors to the game's log when that is reachable.
/// </summary>
/// <remarks>
/// The game log is reached through reflection, so a renamed logging API costs only the mirror, not the
/// plugin. The file log is the one to attach to bug reports. It stays on your machine; Tether never
/// uploads anything.
/// </remarks>
internal sealed class PluginLog : ITetherLog, IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter? _file;
    private readonly LogLevel _minimum;
    private readonly Action<string>? _gameLog;

    public PluginLog(string directory, LogLevel minimum)
    {
        _minimum = minimum;
        FilePath = Path.Combine(directory, "tether.log");
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, Path.Combine(directory, "tether.prev.log"), overwrite: true);
            }

            _file = new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Tether] Could not open log file {FilePath}: {ex.Message}");
        }

        _gameLog = FindGameLog();
    }

    public string FilePath { get; }

    public bool MirrorsToGameLog => _gameLog is not null;

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (level < _minimum)
        {
            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level,-7}] {message}";
        lock (_gate)
        {
            try
            {
                _file?.WriteLine(line);
                if (exception is not null)
                {
                    _file?.WriteLine(exception.ToString());
                }
            }
            catch
            {
                // Logging must never take the game down.
            }
        }

        if (_gameLog is not null && level >= LogLevel.Info)
        {
            try
            {
                _gameLog($"[Tether] {message}{(exception is null ? string.Empty : " | " + exception.Message)}");
            }
            catch
            {
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _file?.Dispose();
        }
    }

    /// <summary>Finds Keen.VRage.Library.Diagnostics.Log.Default.WriteLine(string), if present.</summary>
    private static Action<string>? FindGameLog()
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("Keen.VRage.Library.Diagnostics.Log", throwOnError: false);
                if (type is null)
                {
                    continue;
                }

                var instance = type.GetProperty("Default", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var writeLine = type.GetMethod("WriteLine", BindingFlags.Public | BindingFlags.Instance, new[] { typeof(string) });
                if (instance is not null && writeLine is not null)
                {
                    return message => writeLine.Invoke(instance, new object[] { message });
                }
            }
        }
        catch
        {
        }

        return null;
    }
}
