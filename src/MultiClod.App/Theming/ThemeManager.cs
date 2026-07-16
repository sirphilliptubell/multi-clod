using System;
using System.Windows;
using System.Windows.Media;
using MultiClod.Terminal.Abstractions;

namespace MultiClod.App.Theming;

/// <summary>
/// Owns swapping the app-wide theme resource dictionary (Theming\Themes\*.xaml) into
/// Application.Resources, and translating the same palette into a TerminalPaneTheme for the
/// embedded terminal control (which reads plain Color values, not WPF resources). Every
/// Theme.Color.*/Theme.Brush.* key referenced anywhere in the app must exist in all three
/// dictionaries - see any one of them for the full key list.
/// </summary>
public static class ThemeManager
{
    private static ResourceDictionary? currentThemeDictionary;

    /// <summary>
    /// Swaps in the given theme's resource dictionary. Every Background/Foreground/etc. bound via
    /// DynamicResource across the app picks up the new values immediately - no window reload
    /// needed. Safe to call repeatedly (e.g. every time the user changes the Settings picker).
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        var dictionary = LoadDictionary(theme);

        var merged = Application.Current.Resources.MergedDictionaries;
        if (currentThemeDictionary is not null)
        {
            merged.Remove(currentThemeDictionary);
        }

        merged.Add(dictionary);
        currentThemeDictionary = dictionary;
    }

    /// <summary>
    /// Derives the embedded terminal's chrome colors (background/foreground/cursor/selection) from
    /// the same theme dictionary the rest of the app uses, so a session pane never drifts from the
    /// surrounding chrome. The terminal's actual text colors still come from whatever ANSI codes
    /// the running Claude Code process emits - this only covers the pane's own base colors.
    /// </summary>
    public static TerminalPaneTheme GetTerminalTheme(AppTheme theme)
    {
        var dictionary = LoadDictionary(theme);

        var foreground = (Color)dictionary["Theme.Color.PrimaryForeground"];

        return new TerminalPaneTheme
        {
            Background = (Color)dictionary["Theme.Color.WindowBackground"],
            Foreground = foreground,
            CursorColor = foreground,
            SelectionBackground = (Color)dictionary["Theme.Color.Accent"],
        };
    }

    private static ResourceDictionary LoadDictionary(AppTheme theme) => new()
    {
        Source = new Uri($"pack://application:,,,/MultiClod.App;component/Theming/Themes/{theme}.xaml"),
    };
}
