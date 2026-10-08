using System.Text.Json.Nodes;

namespace Overdrive.Core;

/// <summary>Hidden command-line modes: `Overdrive.exe --selftest | --help | --version`.</summary>
public static class Cli
{
    public static int Run(string[] args)
    {
        var cmd = args[0].ToLowerInvariant();
        switch (cmd)
        {
            case "--help":
            case "-h":
            case "/?":
                PrintHelp();
                return 0;

            case "--version":
            case "-v":
                Console.WriteLine("OVERDRIVE 1.0.0");
                return 0;

            case "--selftest":
                return SelfTest();

            default:
                Console.WriteLine($"overdrive: unknown option '{cmd}'");
                Console.WriteLine();
                PrintHelp();
                return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("OVERDRIVE — Roblox Performance Engine v1.0.0");
        Console.WriteLine();
        Console.WriteLine("  Overdrive.exe              start the GUI");
        Console.WriteLine("  Overdrive.exe --selftest   verify detection, allowlist and files (read-only)");
        Console.WriteLine("  Overdrive.exe --version    print version");
        Console.WriteLine("  Overdrive.exe --help       this text");
    }

    private static void PrintResult(int index, string label, string status, string detail)
        => Console.WriteLine($"[{index,1}/7] {label,-24} {status,-6} {detail}");

    private static int SelfTest()
    {
        Console.WriteLine("OVERDRIVE SELFTEST");
        Console.WriteLine(new string('-', 64));

        var failures = 0;

        // 1 — app data dir writable
        try
        {
            Paths.EnsureAppDir();
            var probe = Path.Combine(Paths.App, "selftest.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            PrintResult(1, "app data dir", "OK", Paths.App);
        }
        catch (Exception ex)
        {
            failures++;
            PrintResult(1, "app data dir", "FAIL", ex.Message);
        }

        // 2 — roblox detection
        RobloxInstall? install = null;
        try
        {
            var config = AppConfig.Load();
            install = RobloxLocator.Find(config.RobloxPath);
            if (install is not null)
                PrintResult(2, "roblox detection", "OK", $"{install.VersionName} → {install.ExePath}");
            else
                PrintResult(2, "roblox detection", "WARN", "no RobloxPlayerBeta.exe found");
        }
        catch (Exception ex)
        {
            failures++;
            PrintResult(2, "roblox detection", "FAIL", ex.Message);
        }

        // 3 — allowlist validation (the whole point of the tool)
        var offList = FlagService.Managed.Where(k => !FlagService.Allowlist.Contains(k)).ToList();
        if (offList.Count > 0)
        {
            failures++;
            PrintResult(3, "fastflag allowlist", "FAIL", "not allowed: " + string.Join(", ", offList));
        }
        else
        {
            var dupes = FlagService.Allowlist.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupes.Count > 0)
            {
                failures++;
                PrintResult(3, "fastflag allowlist", "FAIL", "duplicate entries: " + string.Join(", ", dupes));
            }
            else
            {
                PrintResult(3, "fastflag allowlist", "OK",
                    $"{FlagService.Managed.Length} managed / {FlagService.Allowlist.Length} allowed");
            }
        }

        // 4 — existing ClientAppSettings.json still parses
        try
        {
            var existing = FlagService.TargetPaths(install?.Dir).FirstOrDefault(File.Exists);
            if (existing is null)
            {
                PrintResult(4, "client settings file", "WARN", "none yet (created on first apply)");
            }
            else
            {
                var node = JsonNode.Parse(File.ReadAllText(existing)) as JsonObject
                    ?? throw new Exception("root is not an object");
                var managed = FlagService.Managed.Count(node.ContainsKey);
                PrintResult(4, "client settings file", "OK", $"{node.Count} keys total, {managed} managed by us");
            }
        }
        catch (Exception ex)
        {
            failures++;
            PrintResult(4, "client settings file", "FAIL", ex.Message);
        }

        // 5 — GlobalBasicSettings readable
        try
        {
            if (!File.Exists(Paths.GlobalSettingsXml))
            {
                PrintResult(5, "global settings xml", "WARN", "GlobalBasicSettings_13.xml missing");
            }
            else
            {
                var cap = SettingsFile.ReadInt("FramerateCap", -999);
                var fs = SettingsFile.ReadBool("Fullscreen", false);
                if (cap == -999) { failures++; PrintResult(5, "global settings xml", "FAIL", "FramerateCap not found"); }
                else PrintResult(5, "global settings xml", "OK", $"FramerateCap={cap} Fullscreen={fs.ToString().ToLowerInvariant()}");
            }
        }
        catch (Exception ex)
        {
            failures++;
            PrintResult(5, "global settings xml", "FAIL", ex.Message);
        }

        // 6 — power plan
        try
        {
            var plan = SystemTweaks.ActivePlanName();
            PrintResult(6, "power plan", plan == "UNKNOWN" ? "WARN" : "OK", plan);
        }
        catch (Exception ex)
        {
            failures++;
            PrintResult(6, "power plan", "FAIL", ex.Message);
        }

        // 7 — dry-run payload
        try
        {
            var vm = new Vm();
            var flags = FlagService.Build(vm);
            var json = new JsonObject();
            foreach (var (key, value) in flags) json[key] = JsonValue.Create(value);
            PrintResult(7, "flag payload (dry run)", "OK", $"{flags.Count} keys, default profile = {vm.ProfileName}");
            Console.WriteLine();
            Console.WriteLine(json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            failures++;
            PrintResult(7, "flag payload (dry run)", "FAIL", ex.Message);
        }

        Console.WriteLine(new string('-', 64));
        if (failures == 0)
        {
            Console.WriteLine("SELFTEST PASSED — everything looks good.");
            return 0;
        }
        Console.WriteLine($"SELFTEST FAILED — {failures} problem(s) found.");
        return 1;
    }
}
