// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.IO;

namespace Tether.Plugin.Bootstrap;

/// <summary>
/// Last-resort logging for failures before the normal log exists. Uses only the base library.
/// Writes tether-startup-error.log in <see cref="DefaultDataDirectory"/>.
/// </summary>
internal static class EmergencyLog
{
    public static void Write(string message, Exception exception)
    {
        try
        {
            var directory = DefaultDataDirectory();
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "tether-startup-error.log"),
                $"{DateTime.UtcNow:O} {message}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nothing else we can do without risking the game.
        }

        try
        {
            Console.Error.WriteLine($"[Tether] {message} {exception.Message}");
        }
        catch
        {
        }
    }

    /// <summary>
    /// %APPDATA%\SpaceEngineers2\Tether on Windows (inside the Wine prefix under Proton), or
    /// ~/.config/SpaceEngineers2/Tether on native Linux. GetFolderPath returns an empty string when the folder
    /// doesn't exist yet, which would put files relative to the working directory, so ask it to create the folder
    /// and fall back to the temp folder.
    /// </summary>
    public static string DefaultDataDirectory()
    {
        string appData;
        try
        {
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        }
        catch
        {
            appData = string.Empty;
        }

        if (string.IsNullOrEmpty(appData))
        {
            appData = Path.GetTempPath();
        }

        return Path.Combine(appData, "SpaceEngineers2", "Tether");
    }
}
