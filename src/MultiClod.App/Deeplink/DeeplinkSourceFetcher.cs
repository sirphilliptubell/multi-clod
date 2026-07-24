using System.IO;
using System.Net.Http;

namespace MultiClod.App.Deeplink;

/// <summary>
/// Fetches a deeplink's source (an http(s) URL, downloaded via HttpClient; a UNC or local path,
/// read directly - no network) into a local file so SafeZipExtractor can open it as a zip. Enforces
/// MaxSourceBytes against both any advertised size (Content-Length / FileInfo.Length) and the
/// actual byte count read, so a response/file that lies about (or omits) its size can't bypass it.
/// </summary>
internal static class DeeplinkSourceFetcher
{
    private const long MaxSourceBytes = 200L * 1024 * 1024;
    private const int CopyBufferSize = 81920;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);

    // A single static instance avoids per-request socket exhaustion (standard HttpClient guidance)
    // - this is the only HTTP call site in the app.
    private static readonly HttpClient HttpClient = new() { Timeout = RequestTimeout };

    public static async Task FetchToFileAsync(
        string source,
        string destinationFilePath,
        IProgress<DeeplinkFetchProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            await FetchHttpAsync(uri, destinationFilePath, progress, cancellationToken);
        }
        else
        {
            FetchLocal(source, destinationFilePath, progress);
        }
    }

    private static async Task FetchHttpAsync(
        Uri uri,
        string destinationFilePath,
        IProgress<DeeplinkFetchProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            if (totalBytes > MaxSourceBytes)
            {
                throw new DeeplinkImportException(
                    $"The file at {uri} is too large ({totalBytes} bytes, max {MaxSourceBytes / (1024 * 1024)} MB).");
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = File.Create(destinationFilePath);

            var buffer = new byte[CopyBufferSize];
            var bytesRead = 0L;
            int read;
            while ((read = await responseStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                bytesRead += read;
                if (bytesRead > MaxSourceBytes)
                {
                    throw new DeeplinkImportException(
                        $"The file at {uri} exceeded the {MaxSourceBytes / (1024 * 1024)} MB download limit.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                progress?.Report(new DeeplinkFetchProgress(bytesRead, totalBytes));
            }
        }
        catch (HttpRequestException ex)
        {
            throw new DeeplinkImportException($"Could not download the session from {uri}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeeplinkImportException($"Timed out downloading the session from {uri}.", ex);
        }
    }

    private static void FetchLocal(string source, string destinationFilePath, IProgress<DeeplinkFetchProgress>? progress)
    {
        try
        {
            progress?.Report(new DeeplinkFetchProgress(0, null));

            var info = new FileInfo(source);
            if (!info.Exists)
            {
                throw new DeeplinkImportException($"Could not find a file at {source}.");
            }

            if (info.Length > MaxSourceBytes)
            {
                throw new DeeplinkImportException(
                    $"The file at {source} is too large ({info.Length} bytes, max {MaxSourceBytes / (1024 * 1024)} MB).");
            }

            File.Copy(source, destinationFilePath, overwrite: true);
            progress?.Report(new DeeplinkFetchProgress(info.Length, info.Length));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new DeeplinkImportException($"Could not read the file at {source}: {ex.Message}", ex);
        }
    }
}
