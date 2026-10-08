using System.Diagnostics;

namespace Overdrive.Core;

public static class Launcher
{
    /// <summary>Starts RobloxPlayerBeta, optionally boosting it to HIGH priority,
    /// and waits (bounded) for its main window to appear.</summary>
    public static Process? Launch(RobloxInstall install, bool highPriority, out string error)
    {
        error = "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = install.ExePath,
                WorkingDirectory = install.Dir,
                UseShellExecute = true
            };

            var process = Process.Start(psi);
            if (process is null)
            {
                error = "Process.Start returned nothing";
                return null;
            }

            if (highPriority)
            {
                try { process.PriorityClass = ProcessPriorityClass.High; }
                catch (Exception ex) { LogService.Warn("Priority boost skipped: " + ex.Message); }
            }

            for (var i = 0; i < 150; i++)
            {
                try
                {
                    process.Refresh();
                    if (process.MainWindowHandle != IntPtr.Zero) break;
                }
                catch { break; }
                Thread.Sleep(100);
            }

            return process;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
