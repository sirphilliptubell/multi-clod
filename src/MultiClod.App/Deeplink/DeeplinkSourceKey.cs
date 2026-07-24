using System.IO;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Normalizes a deeplink source (an http(s) URL, a UNC path, or a local path) into a stable string
/// used both to key the in-memory window registry (DeeplinkImportWindowRegistry) and to derive the
/// on-disk extraction folder name (DeeplinkImportStorage) - so re-clicking the same link always
/// maps to the same window/folder regardless of incidental casing differences.
/// </summary>
internal static class DeeplinkSourceKey
{
    public static string Normalize(string source)
    {
        var trimmed = source.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.ToString();
        }

        // Not an http(s) URL - treat as a filesystem/UNC path. Windows paths are case-insensitive,
        // so uppercasing after canonicalizing (GetFullPath resolves "." segments/relative bits and
        // normalizes separators) makes two different-cased references to the same file collapse to
        // the same key.
        try
        {
            return Path.GetFullPath(trimmed).ToUpperInvariant();
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return trimmed.ToUpperInvariant();
        }
    }
}
