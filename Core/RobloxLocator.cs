using System.Diagnostics;

namespace Overdrive.Core;

public sealed class RobloxInstall
{
    public string VersionName { get; init; } = "";
    public string ExePath { get; init; } = "";
    public string Dir { get; init; } = "";
}

/// <summary>Finds the newest installed RobloxPlayerBeta.exe.</summary>
public static class RobloxLocator
{
    public static RobloxInstall? Find(string? overridePath = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
                return FromExe(overridePath);
        }
        catch { /* fall through to normal scan */ }

        foreach (var root in CandidateRoots())
        {
            try
            {
                if (!Directory.Exists(root)) continue;

                RobloxInstall? best = null;
                DateTime bestTime = DateTime.MinValue;

                foreach (var dir in Directory.GetDirectories(root, "version-*"))
                {
                    var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
                    if (!File.Exists(exe)) continue;

                    var time = File.GetLastWriteTimeUtc(exe);
                    if (time > bestTime)
                    {
                        bestTime = time;
                        best = FromExe(exe);
                    }
                }

                if (best is not null) return best;
            }
            catch { /* unreadable root — try the next one */ }
        }

        return null;
    }

    private static IEnumerable<string> CandidateRoots()
    {
        yield return Path.Combine(Paths.Roblox, "Versions");

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf)) yield return Path.Combine(pf, "Roblox", "Versions");

        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pf86)) yield return Path.Combine(pf86, "Roblox", "Versions");
    }

    private static RobloxInstall FromExe(string exe) => new()
    {
        ExePath = exe,
        Dir = Path.GetDirectoryName(exe) ?? "",
        VersionName = new DirectoryInfo(Path.GetDirectoryName(exe) ?? "").Name
    };

    public static bool IsRunning() =>
        Process.GetProcessesByName("RobloxPlayerBeta").Length > 0;

    /// <summary>Asks every Roblox client to close, then kills stragglers after the grace period.</summary>
    public static async Task CloseAllAsync(int graceMs = 6000)
    {
        var procs = Process.GetProcessesByName("RobloxPlayerBeta");
        if (procs.Length == 0) return;

        foreach (var p in procs)
        {
            try { p.CloseMainWindow(); } catch { }
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(graceMs);
        while (DateTime.UtcNow < deadline &&
               procs.Any(p => { try { return !p.HasExited; } catch { return false; } }))
        {
            await Task.Delay(200);
        }

        foreach (var p in procs)
        {
            try
            {
                if (!p.HasExited) p.Kill(entireProcessTree: true);
            }
            catch { /* already gone or protected */ }
            finally { p.Dispose(); }
        }
    }
}
