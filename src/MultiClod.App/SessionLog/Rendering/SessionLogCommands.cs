using System.Windows.Input;

namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Routed command used by TranscriptCategoryStyles.xaml's row templates for the per-row copy
/// button - bound with CommandParameter="{Binding}" (the row itself) and handled by a
/// CommandBinding registered on TranscriptViewerControl, so the DataTemplates don't need their own
/// code-behind to reach the clipboard.
/// </summary>
public static class SessionLogCommands
{
    public static readonly RoutedUICommand CopyEntryJson = new("Copy Entry JSON", nameof(CopyEntryJson), typeof(SessionLogCommands));
}
