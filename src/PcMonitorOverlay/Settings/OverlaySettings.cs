using System.IO;
using System.Text.Json;

namespace PcMonitorOverlay.Settings;

public sealed class OverlaySettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public double Left { get; set; } = 80;
    public double Top { get; set; } = 80;
    public double Width { get; set; } = 520;
    public double Height { get; set; } = 330;
    public double Opacity { get; set; } = 0.92;
    public bool Topmost { get; set; } = true;
    public bool MovementLocked { get; set; }

    private static string SettingsPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PcMonitorOverlay");

            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "settings.json");
        }
    }

    public static OverlaySettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new OverlaySettings();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<OverlaySettings>(json) ?? new OverlaySettings();
        }
        catch
        {
            return new OverlaySettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(SettingsPath, json);
    }
}
