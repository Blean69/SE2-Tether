// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Tether contributors. Game-linking exception: see LICENSE-EXCEPTION.md.

using System;
using System.Reflection;
using Tether.Diagnostics;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Keen.VRage.UI.Screens;
using Tether.Game.Sessions;

namespace Tether.Game.UI;

internal sealed class MultiplayerScreenViewModel : ScreenViewModel
{
    public MultiplayerScreenViewModel()
    {
        KeepsOtherScreensVisible = false;
        AllowsInputBelowUI = false;
        AllowsInputFromLowerScreens = false;
        InitializeInputContext();
    }
}

/// <summary>
/// Tether's Multiplayer panel: who can join the world you host, your join code, and what's happening.
/// Built in code (no XAML) on the game's own screen classes, so it looks and behaves like a game screen.
/// Open it with Ctrl+M in a world, or from the pause menu.
/// </summary>
internal sealed class MultiplayerScreen : ScreenView
{
    private static readonly IBrush Panel = new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x24, 0x2A));
    private static readonly IBrush Edge = new SolidColorBrush(Color.FromArgb(0xFF, 0x42, 0x4D, 0x63));
    private static readonly IBrush Dim = new SolidColorBrush(Color.FromArgb(0xFF, 0xA9, 0xB4, 0xBC));
    private static readonly IBrush Accent = new SolidColorBrush(Color.FromArgb(0xFF, 0x5C, 0xC8, 0xD8));

    private readonly TextBlock _status = Text(18, Brushes.White);
    private readonly TextBlock _code = Text(34, Accent);
    private readonly TextBlock _codeNote = Text(14, Dim);
    private readonly TextBlock _visibilityNote = Text(15, Dim);
    private readonly Button _copy = new() { Content = "Copy code", Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<HostVisibility, Button> _visibilityButtons = new();
    private readonly StackPanel _hostSection = new() { Spacing = 10 };

    public MultiplayerScreen()
    {
        Build();
    }

    /// <summary>
    /// The build's version and commit, e.g. "0.1.0 (66b648e)", so players can check they run the same build.
    /// </summary>
    private static string VersionText()
    {
        var info = typeof(MultiplayerScreen).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        var plus = info.IndexOf('+');
        return plus < 0 ? info : $"{info[..plus]} ({info.Substring(plus + 1, Math.Min(7, info.Length - plus - 1))})";
    }

    /// <summary>Opens the panel on top of whatever is showing.</summary>
    public static void Open()
    {
        var ui = GameAccess.SharedUI();
        if (ui is null)
        {
            GameModule.Log.Warn("UI: the multiplayer panel can't open: the game's UI isn't available yet.");
            return;
        }

        try
        {
            ui.CreateScreen<MultiplayerScreen>(new MultiplayerScreenViewModel(), true);
        }
        catch (Exception ex)
        {
            GameModule.Log.Warn("UI: the multiplayer panel failed to open.", ex);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
    }

    private void Build()
    {
        var title = Text(26, Brushes.White);
        title.Text = "Multiplayer";
        var subtitle = Text(13, Dim);
        subtitle.Text = $"Tether {VersionText()} · unofficial, open source";

        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        close.Click += (_, _) => Dispose();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var titles = new StackPanel { Children = { title, subtitle } };
        Grid.SetColumn(close, 1);
        header.Children.Add(titles);
        header.Children.Add(close);

        // Not an async lambda: the game defines its own AsyncVoidMethodBuilder, so async work is an async Task.
        _copy.Click += (_, _) => _ = CopyCodeAsync();

        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { _code, _copy } };

        var visibilityLabel = Text(16, Brushes.White);
        visibilityLabel.Text = "Who can join";
        var visibilityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var option in new[] { HostVisibility.Hidden, HostVisibility.Private, HostVisibility.Friends, HostVisibility.Public })
        {
            var button = new Button { Content = option.ToString(), MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) =>
            {
                HostedSession.SetVisibility(option);
                Refresh();
            };
            _visibilityButtons[option] = button;
            visibilityRow.Children.Add(button);
        }

        _hostSection.Children.Add(Separator());
        _hostSection.Children.Add(Label("Join code"));
        _hostSection.Children.Add(codeRow);
        _hostSection.Children.Add(_codeNote);
        _hostSection.Children.Add(Separator());
        _hostSection.Children.Add(visibilityLabel);
        _hostSection.Children.Add(visibilityRow);
        _hostSection.Children.Add(_visibilityNote);

        var footer = Text(13, Dim);
        footer.Text = "Steam friends can also join from their friends list (right-click you, Join Game) when the world is set to Friends or Public.";

        var body = new StackPanel { Spacing = 14, Children = { header, _status, _hostSection, Separator(), footer } };

        var card = new Border
        {
            Background = Panel,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1.5),
            Padding = new Thickness(28),
            Width = 640,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = body,
        };

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));
        Content = card;
    }

    private async System.Threading.Tasks.Task CopyCodeAsync()
    {
        var code = HostedSession.Current?.JoinCode?.ToDisplayString();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (code is null || clipboard is null)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(code);
            _codeNote.Text = "Copied. Send it to the people you want to join.";
        }
        catch (Exception ex)
        {
            GameModule.Log.Debug($"UI: copy failed: {ex.Message}");
            _codeNote.Text = "Couldn't copy; type the code instead.";
        }
    }

    private void Refresh()
    {
        var hosted = HostedSession.Current;
        if (hosted is not null)
        {
            _status.Text = HostedSession.Visibility == HostVisibility.Hidden
                ? "You are hosting this world. It is closed: nobody can join."
                : "You are hosting this world. It is open to others.";
            _hostSection.IsVisible = true;
            var code = hosted.JoinCode?.ToDisplayString();
            _code.Text = code ?? "—";
            _copy.IsEnabled = code is not null;
            _codeNote.Text = HostedSession.Visibility == HostVisibility.Public
                ? "Anyone with this code can join from the main menu: Multiplayer, then enter the code."
                : "Joining by code works when the world is set to Public.";
            foreach (var (option, button) in _visibilityButtons)
            {
                var selected = option == HostedSession.Visibility;
                button.Content = selected ? $"● {option}" : option.ToString();
                button.FontWeight = selected ? FontWeight.Bold : FontWeight.Normal;
            }

            _visibilityNote.Text = TetherSettings.Describe(HostedSession.Visibility);
            return;
        }

        _hostSection.IsVisible = false;
        _status.Text = SessionState.ClientDescription
            ?? "No multiplayer session right now. Load a world to host one, or join from the main menu.";
    }

    private static TextBlock Text(double size, IBrush color) =>
        new() { FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Label(string text)
    {
        var label = Text(16, Brushes.White);
        label.Text = text;
        return label;
    }

    private static Border Separator() =>
        new() { Height = 1, Background = Edge, Margin = new Thickness(0, 4, 0, 4) };
}
