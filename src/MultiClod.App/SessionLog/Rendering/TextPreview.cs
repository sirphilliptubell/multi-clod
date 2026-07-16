namespace MultiClod.App.SessionLog.Rendering;

/// <summary>
/// Collapses a block of text to a single line and truncates it for use in a row's collapsed
/// summary - the full text is always still available by expanding the row.
/// </summary>
internal static class TextPreview
{
    private const int MaxLength = 100;

    public static string Truncate(string text)
    {
        var singleLine = text.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= MaxLength ? singleLine : string.Concat(singleLine.AsSpan(0, MaxLength), "...");
    }
}
