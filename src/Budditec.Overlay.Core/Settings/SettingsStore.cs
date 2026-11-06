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

    public static string DefaultPath { get; } = Diagnostics.DevOptions.SettingsPath ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Budditec.Overlay", "settings.json");

    public string Path { get; } = path;

    public SettingsStore() : this(DefaultPath)
    {
    }

    public OverlaySettings Load()
    {
        if (!File.Exists(Path)) return new OverlaySettings();
        try
        {
            return JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(Path), JsonOptions) ?? new OverlaySettings();
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
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, Path, overwrite: true);
    }
}
