namespace MultiClod.App.SessionLog;

/// <summary>
/// Shared categorical color palette for identifying an agent/subagent across the Session Log's
/// Tree and Costs views - kept in one place so both read as the same visual language. Reuses the
/// existing category accent hues (CategoryToBrushConverter).
/// </summary>
internal static class SessionLogPalette
{
    public static readonly IReadOnlyList<string> ConnectorPalette = ["#3A96DD", "#DA7756", "#9B8AC4", "#D08B2C", "#8A8A8A", "#D9A93A"];
}
