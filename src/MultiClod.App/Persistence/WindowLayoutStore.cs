using System.IO;
using System.Text.Json;

namespace MultiClod.App.Persistence;

/// <summary>
/// Owns window-layout.json. Deliberately simpler than <see cref="SessionStore"/> (no debounce or
/// backup rotation) - this is only ever written once, from MainWindow.OnClosing, so there's no
/// rapid-mutation case to collapse and nothing irreplaceable to lose from a rare partial write.
/// </summary>
public sealed class WindowLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string filePath;

    public WindowLayoutStore(string? dataDirectoryOverride = null)
    {
        this.filePath = Path.Combine(dataDirectoryOverride ?? MultiClodDataDirectory.Root, "window-layout.json");
    }

    public WindowLayout? Load()
    {
        if (!File.Exists(this.filePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(this.filePath), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Save(WindowLayout layout)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.filePath)!);
            File.WriteAllText(this.filePath, JsonSerializer.Serialize(layout, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence, matching SessionStore - a locked file or full disk on the
            // way out shouldn't turn "closing the app" into a crash.
        }
    }
}
