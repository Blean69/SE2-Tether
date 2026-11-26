// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Tether.Diagnostics;

namespace Tether.Game.UI;

/// <summary>
/// Adds a "Multiplayer" button to the game's pause menu that opens Tether's Multiplayer panel.
/// </summary>
/// <remarks>
/// <para>
/// The main menu needs nothing from Tether: once the game's built-in multiplayer is switched on, the game shows
/// its own Multiplayer button there, which opens its join-by-code screen.
/// </para>
/// <para>
/// The pause menu is compiled XAML whose buttons only exist after its first layout. Once it has loaded, Tether
/// finds the panel holding the most buttons (the menu list), copies the look of an existing button, and inserts
/// its own before Settings (or at the end). If the layout isn't what it expects, it logs what it found and adds
/// nothing; Ctrl+M still opens the panel.
/// </para>
/// </remarks>
internal static class MenuButtons
{
    private const string Marker = "TetherMultiplayerButton";
    private const string Where = "pause menu";

    public static void PauseMenuScreenPostfix(ContentControl __instance)
    {
        var screen = __instance;
        try
        {
            if (TryInsert(screen))
            {
                return;
            }

            // The buttons are created during the screen's first layout: try again once it has loaded, and once
            // more on the next UI tick in case they are generated after that.
            screen.Loaded += OnLoaded;

            // Runs inside the game's UI event handling: nothing may escape from here.
            void OnLoaded(object? sender, RoutedEventArgs e)
            {
                try
                {
                    screen.Loaded -= OnLoaded;
                    if (Attempt(screen))
                    {
                        return;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            if (!Attempt(screen))
                            {
                                LogLayout(screen);
                            }
                        }
                        catch (Exception ex)
                        {
                            GameModule.Log.Warn($"UI: could not add the Multiplayer button to the {Where}.", ex);
                        }
                    }, DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    GameModule.Log.Warn($"UI: could not add the Multiplayer button to the {Where}.", ex);
                }
            }
        }
        catch (Exception ex)
        {
            GameModule.Log.Warn($"UI: could not add the Multiplayer button to the {Where}.", ex);
        }
    }

    private static bool Attempt(ContentControl screen)
    {
        try
        {
            return TryInsert(screen);
        }
        catch (Exception ex)
        {
            GameModule.Log.Warn($"UI: could not add the Multiplayer button to the {Where}.", ex);
            return true; // Don't retry or dump the layout after a real error.
        }
    }

    private static bool TryInsert(ContentControl screen)
    {
        var buttons = FindButtons(screen);
        if (buttons.Any(b => Equals(b.Tag, Marker)))
        {
            return true;
        }

        if (buttons.Any(b => string.Equals(TextOf(b)?.Trim(), "Multiplayer", StringComparison.OrdinalIgnoreCase)))
        {
            GameModule.Log.Debug($"UI: the {Where} already has a Multiplayer button.");
            return true;
        }

        var menu = buttons
            .Where(b => b.Parent is Panel)
            .GroupBy(b => (Panel)b.Parent!)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();
        if (menu is null || menu.Count() < 3)
        {
            return false;
        }

        var panel = menu.Key;
        if (panel.IsItemsHost)
        {
            GameModule.Log.Warn($"UI: the {Where}'s buttons are generated from a list ({panel.GetType().Name}); the Multiplayer button was not added. Use Ctrl+M.");
            return true;
        }

        var before = menu.FirstOrDefault(b => string.Equals(TextOf(b)?.Trim(), "Settings", StringComparison.OrdinalIgnoreCase));
        var template = before ?? menu.Last();
        var templateText = TextOf(template);
        var button = new Button
        {
            Tag = Marker,
            Content = templateText is { Length: > 0 } && templateText == templateText.ToUpperInvariant() ? "MULTIPLAYER" : "Multiplayer",
            Theme = template.Theme,
            Width = template.Width,
            MinWidth = template.MinWidth,
            Height = template.Height,
            MinHeight = template.MinHeight,
            Margin = template.Margin,
            Padding = template.Padding,
            HorizontalAlignment = template.HorizontalAlignment,
            HorizontalContentAlignment = template.HorizontalContentAlignment,
            VerticalContentAlignment = template.VerticalContentAlignment,
        };
        foreach (var cls in template.Classes.Where(c => !c.StartsWith(':')))
        {
            button.Classes.Add(cls);
        }

        button.Click += (_, _) => MultiplayerScreen.Open();

        var index = before is null ? panel.Children.Count : panel.Children.IndexOf(before);
        panel.Children.Insert(Math.Clamp(index, 0, panel.Children.Count), button);
        GameModule.Log.Info($"UI: added a Multiplayer button to the {Where} ({panel.GetType().Name} with {menu.Count()} buttons).");
        return true;
    }

    private static List<Button> FindButtons(Control root) =>
        root.GetLogicalDescendants().OfType<Button>()
            .Concat(root.GetVisualDescendants().OfType<Button>())
            .Distinct()
            .ToList();

    private static string? TextOf(Button button) => button.Content switch
    {
        string s => s,
        TextBlock t => t.Text,
        _ => button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text,
    };

    private static void LogLayout(ContentControl screen)
    {
        var buttons = FindButtons(screen);
        var summary = string.Join("; ", buttons.Take(20).Select(b => $"{b.Parent?.GetType().Name}>{b.GetType().Name}:{TextOf(b)}"));
        var types = string.Join(", ", screen.GetVisualDescendants()
            .GroupBy(v => v.GetType().Name)
            .OrderByDescending(g => g.Count())
            .Take(25)
            .Select(g => $"{g.Key}×{g.Count()}"));
        GameModule.Log.Warn($"UI: couldn't find the {Where}'s button list ({buttons.Count} buttons: {summary}). The Multiplayer button was not added; use Ctrl+M in a world.");
        GameModule.Log.Debug($"UI: {Where} layout: {types}");
    }
}

/// <summary>What Tether knows about the session it's in as a client.</summary>
internal static class SessionState
{
    public static string? ClientDescription { get; set; }
}
