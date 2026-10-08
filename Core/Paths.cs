namespace Overdrive.Core;

/// <summary>Well-known filesystem locations for OVERDRIVE + Roblox.</summary>
public static class Paths
{
    public static string Local { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>%LOCALAPPDATA%\Overdrive — our config, manifests, backups, log.</summary>
    public static string App { get; } = Path.Combine(Local, "Overdrive");

    /// <summary>%LOCALAPPDATA%\Roblox</summary>
    public static string Roblox { get; } = Path.Combine(Local, "Roblox");

    public static string GlobalSettingsXml => Path.Combine(Roblox, "GlobalBasicSettings_13.xml");
    public static string ClientSettingsDir => Path.Combine(Roblox, "ClientSettings");
    public static string ClientSettingsFile => Path.Combine(ClientSettingsDir, "ClientAppSettings.json");

    public static string ConfigFile => Path.Combine(App, "config.json");
    public static string FlagManifest => Path.Combine(App, "manifest-flags.json");
    public static string TweakManifest => Path.Combine(App, "manifest-tweaks.json");
    public static string SettingsManifest => Path.Combine(App, "manifest-settings.json");
    public static string LogFile => Path.Combine(App, "overdrive.log");
    public static string BackupsDir => Path.Combine(App, "Backups");

    public static void EnsureAppDir() => Directory.CreateDirectory(App);
}
