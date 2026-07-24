namespace MultiClod.App.Deeplink;

/// <summary>
/// A user-facing failure anywhere in the fetch -> extract -> classify pipeline (bad/unreachable
/// source, corrupt zip, zip-slip rejection, size limit exceeded, no importable content found).
/// Message is shown directly in DeeplinkProgressWindow's error state - keep it plain-English.
/// </summary>
internal sealed class DeeplinkImportException : Exception
{
    public DeeplinkImportException(string message)
        : base(message)
    {
    }

    public DeeplinkImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
