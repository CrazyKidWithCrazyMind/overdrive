using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Overdrive.Core;

public sealed record ProfileData(
    int Quality, int Texture, int Msaa, int Api,
    bool Crisp, bool AltFix, bool GrassMotion,
    int Csg0, int Csg12, int Csg23, int Csg34,
    int GrassMax, int GrassMin);

public static class Profiles
{
    /// <summary>Values per loadout. Every loadout writes the same key set, so state on
    /// disk always maps back to exactly one profile (or CUSTOM when hand-edited).</summary>
    public static ProfileData Get(int profile) => profile switch
    {
        // PERFORMANCE — sacrifice looks for frames
        Vm.PerfProfile => new ProfileData(
            Quality: 1, Texture: 1, Msaa: 0, Api: 1,
            Crisp: false, AltFix: true, GrassMotion: true,
            Csg0: 120, Csg12: 130, Csg23: 140, Csg34: 150,
            GrassMax: 250, GrassMin: 100),

        // BALANCED — stock visuals + stability fixes only
        Vm.BalancedProfile => new ProfileData(
            Quality: 5, Texture: 0, Msaa: 0, Api: 1,
            Crisp: false, AltFix: true, GrassMotion: false,
            Csg0: 500, Csg12: 550, Csg23: 600, Csg34: 650,
            GrassMax: 800, GrassMin: 400),

        // SMOOTH — high fidelity, AA on, detail kept far out
        Vm.SmoothProfile => new ProfileData(
            Quality: 8, Texture: 3, Msaa: 4, Api: 1,
            Crisp: false, AltFix: true, GrassMotion: false,
            Csg0: 900, Csg12: 990, Csg23: 1080, Csg34: 1170,
            GrassMax: 1500, GrassMin: 750),

        _ => throw new ArgumentOutOfRangeException(nameof(profile), "CUSTOM has no fixed values")
    };
}

public sealed class ApplyReport
{
    public int Files { get; set; }
    public int Keys { get; set; }
    public List<string> Errors { get; } = new();
    public bool Ok => Errors.Count == 0;
}

public sealed class PrevValue
{
    public bool Had { get; set; }
    public string? Json { get; set; }
}

public sealed class FlagManifest
{
    public Dictionary<string, Dictionary<string, PrevValue>> Files { get; set; } = new();
}

/// <summary>Reads/writes ClientAppSettings.json — strictly limited to Roblox's official
/// FastFlag allowlist (devforum topic 3966569). Anything else is ignored by the client
/// anyway, so writing it would only make the tool look broken.</summary>
public static class FlagService
{
    /// <summary>Roblox's official allowlist, Sept 2025 → present. Used for validation.</summary>
    public static readonly string[] Allowlist =
    {
        "DFIntCSGLevelOfDetailSwitchingDistance",
        "DFIntCSGLevelOfDetailSwitchingDistanceL12",
        "DFIntCSGLevelOfDetailSwitchingDistanceL23",
        "DFIntCSGLevelOfDetailSwitchingDistanceL34",
        "FFlagHandleAltEnterFullscreenManually",
        "DFFlagTextureQualityOverrideEnabled",
        "DFIntTextureQualityOverride",
        "FIntDebugForceMSAASamples",
        "DFFlagDisableDPIScale",
        "FFlagDebugGraphicsPreferD3D11",
        "FFlagDebugSkyGray",
        "DFFlagDebugPauseVoxelizer",
        "DFIntDebugFRMQualityLevelOverride",
        "FIntFRMMaxGrassDistance",
        "FIntFRMMinGrassDistance",
        "FFlagDebugGraphicsPreferVulkan",
        "FFlagDebugGraphicsPreferOpenGL",
        "FIntGrassMovementReducedMotionFactor"
    };

    /// <summary>Keys OVERDRIVE manages. Strict subset of <see cref="Allowlist"/>.</summary>
    public static readonly string[] Managed =
    {
        "DFIntCSGLevelOfDetailSwitchingDistance",
        "DFIntCSGLevelOfDetailSwitchingDistanceL12",
        "DFIntCSGLevelOfDetailSwitchingDistanceL23",
        "DFIntCSGLevelOfDetailSwitchingDistanceL34",
        "FFlagHandleAltEnterFullscreenManually",
        "DFFlagTextureQualityOverrideEnabled",
        "DFIntTextureQualityOverride",
        "FIntDebugForceMSAASamples",
        "DFFlagDisableDPIScale",
        "FFlagDebugGraphicsPreferD3D11",
        "FFlagDebugGraphicsPreferVulkan",
        "FFlagDebugGraphicsPreferOpenGL",
        "DFIntDebugFRMQualityLevelOverride",
        "FIntFRMMaxGrassDistance",
        "FIntFRMMinGrassDistance",
        "FIntGrassMovementReducedMotionFactor"
    };

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static int ManagedCount => Managed.Length;

    public static Dictionary<string, string> Build(ProfileData d) => new()
    {
        ["DFIntCSGLevelOfDetailSwitchingDistance"] = N(d.Csg0),
        ["DFIntCSGLevelOfDetailSwitchingDistanceL12"] = N(d.Csg12),
        ["DFIntCSGLevelOfDetailSwitchingDistanceL23"] = N(d.Csg23),
        ["DFIntCSGLevelOfDetailSwitchingDistanceL34"] = N(d.Csg34),
        ["FFlagHandleAltEnterFullscreenManually"] = B(d.AltFix),
        ["DFFlagTextureQualityOverrideEnabled"] = B(d.Texture > 0),
        ["DFIntTextureQualityOverride"] = N(d.Texture <= 0 ? 2 : d.Texture),
        ["FIntDebugForceMSAASamples"] = N(d.Msaa),
        ["DFFlagDisableDPIScale"] = B(d.Crisp),
        ["FFlagDebugGraphicsPreferD3D11"] = B(d.Api == 1),
        ["FFlagDebugGraphicsPreferVulkan"] = B(d.Api == 2),
        ["FFlagDebugGraphicsPreferOpenGL"] = B(d.Api == 3),
        ["DFIntDebugFRMQualityLevelOverride"] = N(d.Quality),
        ["FIntFRMMaxGrassDistance"] = N(d.GrassMax),
        ["FIntFRMMinGrassDistance"] = N(d.GrassMin),
        ["FIntGrassMovementReducedMotionFactor"] = B(d.GrassMotion)
    };

    public static Dictionary<string, string> Build(Vm vm) =>
        Build(new ProfileData(
            vm.QualityLevel, vm.TextureQuality, vm.Msaa, vm.ApiMode,
            vm.CrispUi, vm.AltEnterFix, vm.GrassMotion,
            vm.Csg0, vm.Csg12, vm.Csg23, vm.Csg34,
            vm.GrassMax, vm.GrassMin));

    /// <summary>Returns which profile the VM currently matches exactly, or CUSTOM.</summary>
    public static int DetectProfile(Vm vm)
    {
        var current = Build(vm);
        foreach (var p in new[] { Vm.PerfProfile, Vm.BalancedProfile, Vm.SmoothProfile })
        {
            var expected = Build(Profiles.Get(p));
            if (expected.Count != current.Count) continue;

            var same = true;
            foreach (var kv in expected)
            {
                if (!current.TryGetValue(kv.Key, out var got) ||
                    !string.Equals(got, kv.Value, StringComparison.Ordinal))
                {
                    same = false;
                    break;
                }
            }
            if (same) return p;
        }
        return Vm.CustomProfile;
    }

    public static List<string> TargetPaths(string? versionDir)
    {
        var list = new List<string> { Paths.ClientSettingsFile };
        if (!string.IsNullOrWhiteSpace(versionDir))
            list.Add(Path.Combine(versionDir, "ClientSettings", "ClientAppSettings.json"));
        return list;
    }

    // ── apply ────────────────────────────────────────────────────────────────

    public static ApplyReport Apply(Vm vm, string? versionDir)
    {
        var flags = Build(vm);
        var report = new ApplyReport();
        var manifest = LoadManifest();

        foreach (var file in TargetPaths(versionDir))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);

                JsonObject node;
                if (File.Exists(file))
                {
                    try
                    {
                        node = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
                    }
                    catch
                    {
                        var evidence = file + ".corrupt";
                        try { File.Copy(file, evidence, true); } catch { }
                        LogService.Warn($"ClientAppSettings.json at {file} was unreadable — kept a copy as {Path.GetFileName(evidence)}, starting fresh.");
                        node = new JsonObject();
                    }
                }
                else
                {
                    node = new JsonObject();
                }

                BackupOnce(file);

                if (!manifest.Files.TryGetValue(file, out var prevs))
                    manifest.Files[file] = prevs = new Dictionary<string, PrevValue>();

                foreach (var key in Managed)
                {
                    if (!prevs.ContainsKey(key))
                    {
                        var existing = node[key];
                        prevs[key] = new PrevValue
                        {
                            Had = existing is not null,
                            Json = existing?.ToJsonString()
                        };
                    }
                    node[key] = JsonValue.Create(flags[key]);
                }

                File.WriteAllText(file, node.ToJsonString(JsonOpts), Utf8NoBom);
                report.Files++;
                report.Keys = Managed.Length;
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{file}: {ex.Message}");
            }
        }

        SaveManifest(manifest);
        return report;
    }

    // ── restore ──────────────────────────────────────────────────────────────

    public static ApplyReport Restore(string? versionDir)
    {
        var report = new ApplyReport();
        var manifest = LoadManifest();
        var known = new HashSet<string>(TargetPaths(versionDir));

        foreach (var (file, prevs) in manifest.Files)
        {
            try
            {
                if (!File.Exists(file))
                {
                    LogService.Info($"No longer present, nothing to restore: {file}");
                    continue;
                }

                JsonObject node;
                try { node = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!; }
                catch { report.Errors.Add($"{file}: unreadable, restore skipped"); continue; }

                foreach (var (key, prev) in prevs)
                {
                    if (prev.Had && prev.Json is not null)
                        node[key] = JsonNode.Parse(prev.Json);
                    else
                        node.Remove(key);
                }

                if (node.Count == 0)
                    File.Delete(file);
                else
                    File.WriteAllText(file, node.ToJsonString(JsonOpts), Utf8NoBom);

                report.Files++;
                report.Keys = prevs.Count;
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{file}: {ex.Message}");
            }
        }

        // Manifest entries for version folders that no longer exist are stale by definition.
        foreach (var stale in manifest.Files.Keys.Where(k => !File.Exists(k) && !known.Contains(k)).ToList())
            manifest.Files.Remove(stale);

        if (manifest.Files.Count == 0)
        {
            try { if (File.Exists(Paths.FlagManifest)) File.Delete(Paths.FlagManifest); } catch { }
        }
        else
        {
            SaveManifest(manifest);
        }

        return report;
    }

    // ── hydrate VM from disk ─────────────────────────────────────────────────

    /// <summary>Reads whatever is currently on disk into the VM. Returns false when no
    /// ClientAppSettings.json exists yet (VM keeps its defaults).</summary>
    public static bool Hydrate(Vm vm, string? versionDir)
    {
        foreach (var file in TargetPaths(versionDir))
        {
            if (!File.Exists(file)) continue;

            JsonObject? node;
            try { node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject; }
            catch { continue; }
            if (node is null) continue;

            vm.Hydrate(v => ReadInto(node, v));
            return true;
        }
        return false;
    }

    private static void ReadInto(JsonObject n, Vm v)
    {
        static int Num(JsonObject n, string key, int fallback)
        {
            if (n[key] is not JsonValue val) return fallback;
            if (val.TryGetValue<int>(out var i)) return i;
            if (val.TryGetValue<string>(out var s) && int.TryParse(s, out var j)) return j;
            return fallback;
        }

        static bool Bool(JsonObject n, string key, bool fallback)
        {
            if (n[key] is not JsonValue val) return fallback;
            if (val.TryGetValue<bool>(out var b)) return b;
            if (val.TryGetValue<string>(out var s))
                return s.Equals("true", StringComparison.OrdinalIgnoreCase);
            return fallback;
        }

        v.QualityLevel = Math.Clamp(Num(n, "DFIntDebugFRMQualityLevelOverride", v.QualityLevel), 1, 10);

        var texEnabled = Bool(n, "DFFlagTextureQualityOverrideEnabled", v.TextureQuality > 0);
        var texValue = Math.Clamp(Num(n, "DFIntTextureQualityOverride", 2), 1, 3);
        v.TextureQuality = texEnabled ? texValue : 0;

        var msaa = Num(n, "FIntDebugForceMSAASamples", v.Msaa);
        v.Msaa = msaa is 0 or 2 or 4 ? msaa : 0;

        v.CrispUi = Bool(n, "DFFlagDisableDPIScale", v.CrispUi);
        v.AltEnterFix = Bool(n, "FFlagHandleAltEnterFullscreenManually", v.AltEnterFix);
        v.GrassMotion = Bool(n, "FIntGrassMovementReducedMotionFactor", v.GrassMotion);

        v.ApiMode =
            Bool(n, "FFlagDebugGraphicsPreferVulkan", false) ? 2 :
            Bool(n, "FFlagDebugGraphicsPreferOpenGL", false) ? 3 :
            Bool(n, "FFlagDebugGraphicsPreferD3D11", false) ? 1 : 0;

        v.Csg0 = Num(n, "DFIntCSGLevelOfDetailSwitchingDistance", v.Csg0);
        v.Csg12 = Num(n, "DFIntCSGLevelOfDetailSwitchingDistanceL12", v.Csg12);
        v.Csg23 = Num(n, "DFIntCSGLevelOfDetailSwitchingDistanceL23", v.Csg23);
        v.Csg34 = Num(n, "DFIntCSGLevelOfDetailSwitchingDistanceL34", v.Csg34);
        v.GrassMax = Num(n, "FIntFRMMaxGrassDistance", v.GrassMax);
        v.GrassMin = Num(n, "FIntFRMMinGrassDistance", v.GrassMin);
    }

    // ── manifest + backup helpers ────────────────────────────────────────────

    private static void BackupOnce(string file)
    {
        try
        {
            if (!File.Exists(file)) return;
            Directory.CreateDirectory(Paths.BackupsDir);
            var bak = Path.Combine(Paths.BackupsDir, "ClientAppSettings." + Slug(file) + ".json");
            if (!File.Exists(bak)) File.Copy(file, bak);
        }
        catch { /* backup is best effort */ }
    }

    private static string Slug(string path)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }

    private static FlagManifest LoadManifest()
    {
        try
        {
            if (File.Exists(Paths.FlagManifest))
                return JsonSerializer.Deserialize<FlagManifest>(File.ReadAllText(Paths.FlagManifest)) ?? new FlagManifest();
        }
        catch { }
        return new FlagManifest();
    }

    private static void SaveManifest(FlagManifest manifest)
    {
        try
        {
            Paths.EnsureAppDir();
            File.WriteAllText(Paths.FlagManifest, JsonSerializer.Serialize(manifest, JsonOpts), Utf8NoBom);
        }
        catch { }
    }

    private static string N(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string B(bool value) => value ? "True" : "False";
}
