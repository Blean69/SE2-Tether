// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System.Reflection;

namespace Tether;

public static class TetherVersion
{
    /// <summary>The informational version of this build, e.g. "0.1.0+&lt;commit&gt;".</summary>
    public static string Plugin { get; } =
        typeof(TetherVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(TetherVersion).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
