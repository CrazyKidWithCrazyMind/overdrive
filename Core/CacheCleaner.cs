namespace Overdrive.Core;

public sealed record CacheBucket(string Key, string Title, string Detail, string Path, bool Manual);

/// <summary>Measures and clears Roblox caches. Only ever touches %LOCALAPPDATA%\Roblox
/// and %TEMP%\Roblox — never game saves, never other applications.</summary>
public static class CacheCleaner
{
    public static List<CacheBucket> All() => new()
    {
        new("temp", "Roblox temp files", "%TEMP%\\Roblox",
            Path.Combine(Path.GetTempPath(), "Roblox"), Manual: false),
        new("logs", "Roblox logs", "%LOCALAPPDATA%\\Roblox\\logs",
            Path.Combine(Paths.Roblox, "logs"), Manual: false),
        new("download", "Download cache", "%LOCALAPPDATA%\\Roblox\\Downloads",
            Path.Combine(Paths.Roblox, "Downloads"), Manual: true),
        new("assets", "Asset cache (rbx-storage)", "%LOCALAPPDATA%\\Roblox\\rbx-storage",
            Path.Combine(Paths.Roblox, "rbx-storage"), Manual: true)
    };

    public static CacheBucket Bucket(string key) =>
        All().FirstOrDefault(b => b.Key == key)
        ?? throw new ArgumentException("unknown cache bucket: " + key);

    public static string SizeString(params string[] keys)
        => FormatSize(keys.Sum(k => SizeOf(Bucket(k).Path)));

    public static long SizeOf(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (!Directory.Exists(path)) return 0;

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { /* locked */ }
            }
            return total;
        }
        catch { return 0; }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.0} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):0} KB";
        return $"{bytes} B";
    }

    /// <summary>Safe subset cleared automatically before every launch.</summary>
    public static long AutoClean() => Clean(new[] { "temp", "logs" });

    public static long Clean(IEnumerable<string> keys)
    {
        long freed = 0;
        foreach (var key in keys)
        {
            var bucket = Bucket(key);
            var before = SizeOf(bucket.Path);
            try
            {
                switch (bucket.Key)
                {
                    case "logs":
                        DeleteOldLogs(bucket.Path);
                        break;
                    case "assets":
                        DeleteDirectory(bucket.Path);
                        DeleteAssetDbFiles();
                        break;
                    default:
                        DeleteDirectory(bucket.Path);
                        break;
                }

                var after = SizeOf(bucket.Path);
                var got = Math.Max(0, before - after);
                freed += got;
                LogService.Ok($"CLEAN {bucket.Title} — freed {FormatSize(got)}.");
            }
            catch (Exception ex)
            {
                LogService.Warn($"CLEAN {bucket.Title} failed: {ex.Message}");
            }
        }
        return freed;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;

        // Best effort: delete files first so a single locked file doesn't block the rest.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { File.Delete(file); } catch { /* locked — skip */ }
        }

        try { Directory.Delete(path, recursive: true); }
        catch { /* leftover dirs are harmless */ }
    }

    private static void DeleteOldLogs(string path)
    {
        if (!Directory.Exists(path)) return;
        var cutoff = DateTime.UtcNow.AddDays(-3);
        foreach (var file in Directory.EnumerateFiles(path))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
            catch { }
        }
    }

    private static void DeleteAssetDbFiles()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Paths.Roblox, "rbx-storage.db*"))
            {
                try { File.Delete(file); } catch { }
            }
        }
        catch { }
    }
}
