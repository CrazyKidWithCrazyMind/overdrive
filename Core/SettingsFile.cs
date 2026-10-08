using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Overdrive.Core;

public sealed class SettingPrev
{
    public string Tag { get; set; } = "int";
    public bool Had { get; set; }
    public string? Value { get; set; }
}

public sealed class SettingsManifest
{
    public Dictionary<string, SettingPrev> Keys { get; set; } = new();
}

/// <summary>Reads/writes scalars inside %LOCALAPPDATA%\Roblox\GlobalBasicSettings_13.xml
/// (Roblox's own settings file). Only touches the exact keys we track, so everything
/// else the user configured in-game stays intact. Each key's pre-OVERDRIVE value is
/// recorded once so RESTORE STOCK can put it back.</summary>
public static class SettingsFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ── read ─────────────────────────────────────────────────────────────────

    public static int ReadInt(string name, int fallback)
    {
        try
        {
            var m = Match(name, "int");
            return m.Success && int.TryParse(m.Groups[2].Value.Trim(), out var v) ? v : fallback;
        }
        catch { return fallback; }
    }

    public static bool ReadBool(string name, bool fallback)
    {
        try
        {
            var m = Match(name, "bool");
            return m.Success
                ? m.Groups[2].Value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
                : fallback;
        }
        catch { return fallback; }
    }

    private static Match Match(string name, string tag)
    {
        if (!File.Exists(Paths.GlobalSettingsXml)) return System.Text.RegularExpressions.Match.Empty;
        var text = File.ReadAllText(Paths.GlobalSettingsXml);
        return Regex.Match(text,
            $@"(<{tag}\s+name=""{Regex.Escape(name)}"">)([^<]*)(</{tag}>)",
            RegexOptions.IgnoreCase);
    }

    // ── write ────────────────────────────────────────────────────────────────

    public static bool WriteInt(string name, int value, out string error)
        => Write(name, "int", value.ToString(), out error);

    public static bool WriteBool(string name, bool value, out string error)
        => Write(name, "bool", value ? "true" : "false", out error);

    private static bool Write(string name, string tag, string value, out string error)
    {
        error = "";
        var file = Paths.GlobalSettingsXml;

        if (!File.Exists(file))
        {
            error = "GlobalBasicSettings_13.xml not found";
            return false;
        }

        try
        {
            var text = File.ReadAllText(file);

            // Record the pre-OVERDRIVE value the first time we touch this key.
            Record(name, tag, text);
            BackupOnce(file);

            var pattern = $@"(<{tag}\s+name=""{Regex.Escape(name)}"">)[^<]*(</{tag}>)";

            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                text = Regex.Replace(text, pattern,
                    m => m.Groups[1].Value + value + m.Groups[2].Value,
                    RegexOptions.IgnoreCase);
            }
            else
            {
                // Key missing entirely: insert it into the first <Properties> block,
                // which is where Roblox keeps the UserGameSettings scalars.
                var close = Regex.Match(text, @"</Properties>");
                if (!close.Success)
                {
                    error = "unexpected XML structure (no <Properties> block)";
                    return false;
                }
                var insert = $"\t\t<{tag} name=\"{name}\">{value}</{tag}>\r\n\t\t";
                text = text.Insert(close.Index, insert);
            }

            File.WriteAllText(file, text, Utf8NoBom);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ── restore ──────────────────────────────────────────────────────────────

    public static ApplyReport Restore()
    {
        var report = new ApplyReport();
        var manifest = LoadManifest();
        if (manifest.Keys.Count == 0) return report;

        var file = Paths.GlobalSettingsXml;
        if (!File.Exists(file))
        {
            report.Errors.Add("GlobalBasicSettings_13.xml not found");
            return report;
        }

        try
        {
            var text = File.ReadAllText(file);

            foreach (var (name, prev) in manifest.Keys)
            {
                var pattern = $@"<{prev.Tag}\s+name=""{Regex.Escape(name)}"">[^<]*</{prev.Tag}>";
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                {
                    if (prev.Had && prev.Value is not null)
                    {
                        text = Regex.Replace(text, pattern,
                            $"<{prev.Tag} name=\"{name}\">{Regex.Escape(prev.Value)}</{prev.Tag}>",
                            RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        // We inserted this key — take it back out.
                        text = Regex.Replace(text, @"\s*" + pattern, "", RegexOptions.IgnoreCase);
                    }
                    report.Keys++;
                }
            }

            File.WriteAllText(file, text, Utf8NoBom);
            report.Files = 1;
        }
        catch (Exception ex)
        {
            report.Errors.Add(ex.Message);
        }

        try { if (File.Exists(Paths.SettingsManifest)) File.Delete(Paths.SettingsManifest); } catch { }
        return report;
    }

    // ── hydrate ──────────────────────────────────────────────────────────────

    public static void Hydrate(Vm vm)
    {
        var cap = ReadInt("FramerateCap", -1);
        vm.FpsCapIndex = Math.Max(0, Array.IndexOf(Vm.FpsCaps, cap));
        vm.Fullscreen = ReadBool("Fullscreen", vm.Fullscreen);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void Record(string name, string tag, string text)
    {
        try
        {
            var manifest = LoadManifest();
            if (manifest.Keys.ContainsKey(name)) return;

            var m = Regex.Match(text,
                $@"(<{tag}\s+name=""{Regex.Escape(name)}"">)([^<]*)(</{tag}>)",
                RegexOptions.IgnoreCase);

            manifest.Keys[name] = new SettingPrev
            {
                Tag = tag,
                Had = m.Success,
                Value = m.Success ? m.Groups[2].Value.Trim() : null
            };

            Paths.EnsureAppDir();
            File.WriteAllText(Paths.SettingsManifest, JsonSerializer.Serialize(manifest, JsonOpts), Utf8NoBom);
        }
        catch { /* recording must never block the write */ }
    }

    private static void BackupOnce(string file)
    {
        try
        {
            Directory.CreateDirectory(Paths.BackupsDir);
            var bak = Path.Combine(Paths.BackupsDir, "GlobalBasicSettings_13.xml.bak");
            if (!File.Exists(bak)) File.Copy(file, bak);
        }
        catch { }
    }

    private static SettingsManifest LoadManifest()
    {
        try
        {
            if (File.Exists(Paths.SettingsManifest))
                return JsonSerializer.Deserialize<SettingsManifest>(File.ReadAllText(Paths.SettingsManifest)) ?? new SettingsManifest();
        }
        catch { }
        return new SettingsManifest();
    }
}
