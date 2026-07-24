namespace MultiClod.App.Deeplink;

/// <summary>
/// Parses a multi-clod://open-session-log?url=&lt;encoded-source&gt; launch argument. The action
/// ("open-session-log") lives in host position, not path - a URI with a "//" authority marker
/// parses that segment as Uri.Host regardless of the scheme being unregistered/custom.
/// </summary>
internal static class DeeplinkUri
{
    public static bool TryParse(string rawArgument, out string source)
    {
        source = string.Empty;

        if (!Uri.TryCreate(rawArgument, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, DeeplinkProtocol.UriScheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, DeeplinkProtocol.OpenSessionLogHost, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var query = uri.Query.TrimStart('?');
        if (query.Length == 0)
        {
            return false;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = pair[..separatorIndex];
            if (!string.Equals(key, DeeplinkProtocol.SourceQueryParameterName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            source = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            return source.Length > 0;
        }

        return false;
    }
}
