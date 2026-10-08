using System.Text.Json;

namespace Overdrive.Core;

/// <summary>Preferences that live only inside OVERDRIVE (not in the system).</summary>
public sealed class AppConfig
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public bool AutoClean { get; set; } = true;
    public bool HighPriority { get; set; } = true;
    public string? RobloxPath { get; set; }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Paths.ConfigFile))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(Paths.ConfigFile)) ?? new AppConfig();
        }
        catch { /* corrupt config is not worth failing over */ }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Paths.EnsureAppDir();
            File.WriteAllText(Paths.ConfigFile, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* non-fatal */ }
    }
}
