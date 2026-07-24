namespace MultiClod.App.Deeplink;

/// <summary>
/// Constants for the multi-clod:// URI scheme - "multi-clod://open-session-log?url=&lt;source&gt;"
/// launches/activates the app and imports a shared Claude Code session transcript zip from
/// &lt;source&gt; (an http(s) URL, a UNC path, or a local path). See DeeplinkInstaller for the
/// registry registration and DeeplinkUri for parsing an incoming launch argument.
/// </summary>
internal static class DeeplinkProtocol
{
    public const string UriScheme = "multi-clod";

    public const string OpenSessionLogHost = "open-session-log";

    public const string SourceQueryParameterName = "url";
}
