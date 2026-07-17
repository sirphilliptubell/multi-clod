using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiClod.App.Native;

// Shared MenuItem/visual-tree helpers for the app's several hand-built context menus (main
// session tree, context/skills tree, session log window) - kept in one place so each window's
// ContextMenuOpening handler stays a short list of Items.Add calls.
internal static class ContextMenuHelper
{
    public static MenuItem CreateMenuItem(string header, Action action, bool enabled = true, string? inputGestureText = null)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled, InputGestureText = inputGestureText };
        item.Click += (_, _) => action();
        return item;
    }

    public static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null and not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }
}
