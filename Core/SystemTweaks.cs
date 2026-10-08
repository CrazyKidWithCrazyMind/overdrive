using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Overdrive.Core;

public sealed class TweakManifest
{
    public string? PowerPrev { get; set; }
    public bool PowerTouched { get; set; }
    public bool GameModeTouched { get; set; }
    public int? GameModePrev { get; set; }
    public bool DvrTouched { get; set; }
    public int? DvrPrev { get; set; }
}

/// <summary>Windows-side tweaks. HKCU-only + powercfg, so nothing here needs admin
/// rights, and every change is recorded so RESTORE STOCK can put it back.</summary>
public static class SystemTweaks
{
    public const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    public const string BalancedGuid = "381cb422-d244-4ec1-8a23-61c487885b65";

    private const string GameBarKey = @"Software\Microsoft\GameBar";
    private const string GameCfgKey = @"System\GameConfigStore";
    private const string GameModeValue = "AutoGameModeEnabled";
    private const string DvrValue = "GameDVR_Enabled";

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ── power plan ───────────────────────────────────────────────────────────

    public static string ActivePlanGuid()
    {
        var output = RunPowercfg("/getactivescheme");
        var m = Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
    }

    public static bool IsHighPerf() => ActivePlanGuid() == HighPerfGuid;

    public static string ActivePlanName()
    {
        var guid = ActivePlanGuid();
        if (guid == HighPerfGuid) return "HIGH PERFORMANCE";
        if (guid == BalancedGuid) return "BALANCED";
        return guid.Length == 0 ? "UNKNOWN" : "CUSTOM PLAN";
    }

    private static string RunPowercfg(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            return output;
        }
        catch { return ""; }
    }

    private static bool SetActive(string guid, out string error)
    {
        error = "";
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", "/setactive " + guid)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(10000);
            if (p.ExitCode == 0) return true;
            error = (stderr + " " + stdout).Trim();
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // ── registry helpers ─────────────────────────────────────────────────────

    private static int? ReadDword(string path, string name)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(path);
            return k?.GetValue(name) is int v ? v : null;
        }
        catch { return null; }
    }

    private static void WriteDword(string path, string name, int value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(path)
            ?? throw new InvalidOperationException($"cannot open HKCU\\{path}");
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void DeleteValue(string path, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(path, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    // ── hydrate / apply / restore ────────────────────────────────────────────

    /// <summary>Mirrors the real system state into the toggles.</summary>
    public static void Hydrate(Vm vm)
    {
        vm.TwPower = IsHighPerf();
        // Missing values mean "Windows default" = Game Mode on, DVR capture on.
        vm.TwGameMode = (ReadDword(GameBarKey, GameModeValue) ?? 1) != 0;
        vm.TwDvr = (ReadDword(GameCfgKey, DvrValue) ?? 1) != 0;
    }

    public static ApplyReport Apply(Vm vm)
    {
        var report = new ApplyReport();
        var manifest = LoadManifest();

        // Power plan -------------------------------------------------------
        try
        {
            var wantHigh = vm.TwPower;
            var isHigh = IsHighPerf();
            if (wantHigh != isHigh)
            {
                if (!manifest.PowerTouched)
                {
                    manifest.PowerPrev = ActivePlanGuid();
                    manifest.PowerTouched = true;
                }

                var target = wantHigh ? HighPerfGuid : BalancedGuid;
                if (SetActive(target, out var err))
                {
                    report.Files++;
                    LogService.Ok(wantHigh
                        ? "Power plan → High performance (no idle throttling)."
                        : "Power plan → Balanced.");
                }
                else
                {
                    report.Errors.Add("powercfg: " + err);
                    LogService.Warn("Could not switch power plan: " + err);
                }
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add("power: " + ex.Message);
        }

        // Game Mode --------------------------------------------------------
        try
        {
            var raw = ReadDword(GameBarKey, GameModeValue);
            var want = vm.TwGameMode ? 1 : 0;
            if ((raw ?? 1) != want)
            {
                if (!manifest.GameModeTouched)
                {
                    manifest.GameModePrev = raw;
                    manifest.GameModeTouched = true;
                }
                WriteDword(GameBarKey, GameModeValue, want);
                report.Files++;
                LogService.Ok($"Windows Game Mode → {(want != 0 ? "ON" : "OFF")}.");
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add("gamemode: " + ex.Message);
            LogService.Warn("Game Mode write failed: " + ex.Message);
        }

        // Xbox Game DVR ----------------------------------------------------
        try
        {
            var raw = ReadDword(GameCfgKey, DvrValue);
            var want = vm.TwDvr ? 1 : 0;
            if ((raw ?? 1) != want)
            {
                if (!manifest.DvrTouched)
                {
                    manifest.DvrPrev = raw;
                    manifest.DvrTouched = true;
                }
                WriteDword(GameCfgKey, DvrValue, want);
                report.Files++;
                LogService.Ok($"Xbox Game DVR background capture → {(want != 0 ? "ON" : "OFF")}.");
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add("dvr: " + ex.Message);
            LogService.Warn("Game DVR write failed: " + ex.Message);
        }

        SaveManifest(manifest);
        return report;
    }

    public static ApplyReport Restore()
    {
        var report = new ApplyReport();
        var manifest = LoadManifest();

        try
        {
            if (manifest.PowerTouched && manifest.PowerPrev is { Length: > 0 } prev)
            {
                if (SetActive(prev, out var err))
                {
                    report.Files++;
                    LogService.Ok($"Power plan restored → {ActivePlanName()}.");
                }
                else
                {
                    report.Errors.Add("powercfg: " + err);
                }
            }

            if (manifest.GameModeTouched)
            {
                if (manifest.GameModePrev is int gm) WriteDword(GameBarKey, GameModeValue, gm);
                else DeleteValue(GameBarKey, GameModeValue);
                report.Files++;
                LogService.Ok("Game Mode restored to system default.");
            }

            if (manifest.DvrTouched)
            {
                if (manifest.DvrPrev is int dvr) WriteDword(GameCfgKey, DvrValue, dvr);
                else DeleteValue(GameCfgKey, DvrValue);
                report.Files++;
                LogService.Ok("Game DVR setting restored to system default.");
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add("restore: " + ex.Message);
        }

        try { if (File.Exists(Paths.TweakManifest)) File.Delete(Paths.TweakManifest); } catch { }
        return report;
    }

    // ── manifest ─────────────────────────────────────────────────────────────

    private static TweakManifest LoadManifest()
    {
        try
        {
            if (File.Exists(Paths.TweakManifest))
                return JsonSerializer.Deserialize<TweakManifest>(File.ReadAllText(Paths.TweakManifest)) ?? new TweakManifest();
        }
        catch { }
        return new TweakManifest();
    }

    private static void SaveManifest(TweakManifest manifest)
    {
        try
        {
            Paths.EnsureAppDir();
            File.WriteAllText(Paths.TweakManifest, JsonSerializer.Serialize(manifest, JsonOpts));
        }
        catch { }
    }
}
