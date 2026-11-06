using System.Text.Json;
using System.Text.Json.Serialization;

namespace Budditec.Overlay.Core.Settings;

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// %AppData% on Windows, ~/Library/Application Support on macOS, $XDG_CONFIG_HOME or ~/.config on Linux.
    /// </summary>
    public static string DefaultPath { get; } = Diagnostics.DevOptions.SettingsPath ?? System.IO.Path.Combine(
        OperatingSystem.IsMacOS()
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Budditec.Overlay", "settings.json");

    public string Path { get; } = path;

    public SettingsStore() : this(DefaultPath)
    {
    }

    public OverlaySettings Load()
    {
        if (!File.Exists(Path)) return new OverlaySettings();
        try
        {
            var settings = JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(Path), JsonOptions) ?? new OverlaySettings();
            settings.Upgrade();
            return settings;
        }
        catch (JsonException)
        {
            return new OverlaySettings();
        }
    }

    public void Save(OverlaySettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, Serialize(settings));
        File.Move(temp, Path, overwrite: true);
    }

    public static string Serialize(OverlaySettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    public static OverlaySettings Clone(OverlaySettings settings) =>
        JsonSerializer.Deserialize<OverlaySettings>(Serialize(settings), JsonOptions)!;
}
