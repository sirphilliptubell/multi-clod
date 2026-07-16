using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MultiClod.App.Persistence;

/// <summary>
/// Owns settings.json. Mirrors <see cref="WindowLayoutStore"/> - no debounce or backup rotation,
/// since settings only change on an explicit user action in SettingsView, not from rapid mutation.
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Otherwise ClaudePermissionMode round-trips as a bare int in settings.json - unreadable
        // to a user poking at the file, and silent about which mode 0/1/2/3 even mean.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string filePath;

    public AppSettingsStore(string? dataDirectoryOverride = null)
    {
        this.filePath = Path.Combine(dataDirectoryOverride ?? MultiClodDataDirectory.Root, "settings.json");
    }

    /// <summary>
    /// Never returns null - a missing or corrupt file just means "defaults", same as a first run.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(this.filePath))
        {
            return new AppSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(this.filePath), JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.filePath)!);
            File.WriteAllText(this.filePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence, matching WindowLayoutStore - a locked file or full disk
            // shouldn't turn toggling a setting into a crash.
        }
    }
}
