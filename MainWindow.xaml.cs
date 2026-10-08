using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Overdrive.Core;

namespace Overdrive;

public partial class MainWindow : Window
{
    private const string IdleHint = "Applies your loadout, then launches Roblox";

    private readonly Vm _vm;
    private readonly DispatcherTimer _statusTimer;
    private RobloxInstall? _install;
    private bool _busy;
    private bool _initialized;
    private bool? _lastRobloxRunning;
    private int _page;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new Vm();
        DataContext = _vm;
        LogService.OnEntry += OnLogEntry;

        // live watchdog: keeps the status pill in sync with the real client state
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += async (_, _) => await PollRobloxRunningAsync();
        _statusTimer.Start();
    }

    // ═══════════════════ log ═══════════════════

    private void OnLogEntry(LogEntry entry)
    {
        try
        {
            Dispatcher.Invoke(() =>
            {
                _vm.Logs.Add(entry);
                while (_vm.Logs.Count > 500)
                    _vm.Logs.RemoveAt(0);
                try { LogScroll.ScrollToEnd(); } catch { }
            });
        }
        catch { /* window may be closing */ }
    }

    // ═══════════════════ startup ═══════════════════

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LogService.Info("OVERDRIVE 1.0.0 — Roblox Performance Engine.");
        LogService.Info("Scanning for RobloxPlayerBeta.exe…");

        var result = await Task.Run(() =>
        {
            var install = RobloxLocator.Find(_vm.RobloxPathOverride);
            var gotFlags = FlagService.Hydrate(_vm, install?.Dir);
            SettingsFile.Hydrate(_vm);
            SystemTweaks.Hydrate(_vm);
            return (install, gotFlags);
        });

        _install = result.install;

        if (_install is null)
        {
            _vm.RobloxVersion = "NOT FOUND";
            _vm.RobloxPath = "Use BROWSE to select RobloxPlayerBeta.exe";
            LogService.Warn("Roblox not found — press BROWSE on the INSTALL card to point at RobloxPlayerBeta.exe.");
        }
        else
        {
            _vm.RobloxVersion = _install.VersionName;
            _vm.RobloxPath = _install.ExePath;
            LogService.Ok($"Detected {_install.VersionName}.");
            LogService.Info(result.gotFlags
                ? "Loaded existing FastFlag overrides from disk."
                : "No local FastFlags yet — PLAY ROBLOX applies your loadout first.");
        }

        _vm.PowerPlan = SystemTweaks.ActivePlanName();
        LogService.Info($"Power plan: {_vm.PowerPlan} · profile: {_vm.ProfileName} · {_vm.FlagsCount} managed flags.");

        UpdateStatus();
        await UpdateCacheAsync();
        LogService.Info($"Cache: temp {_vm.CacheSize} · assets {_vm.AssetSize} · downloads {_vm.DownloadSize}.");

        _lastRobloxRunning = RobloxLocator.IsRunning(); // don't log state that was already true
        _initialized = true;
    }

    private void UpdateStatus()
    {
        if (_install is null)
            _vm.SetStatus("ROBLOX NOT FOUND", "bad");
        else if (RobloxLocator.IsRunning())
            _vm.SetStatus($"ROBLOX RUNNING · {ShortVer(_install)}", "warn");
        else
            _vm.SetStatus($"{ShortVer(_install)} · READY", "ready");
    }

    /// <summary>Runs every second: flips the status pill the moment the client starts
    /// or stops, logs the transition, and re-scans the install after Roblox closes
    /// (in case the client updated itself while it was open).</summary>
    private async Task PollRobloxRunningAsync()
    {
        if (!_initialized) return;

        var running = RobloxLocator.IsRunning();
        if (_lastRobloxRunning == running) return;

        var firstSighting = _lastRobloxRunning is null;
        _lastRobloxRunning = running;
        UpdateStatus();

        if (firstSighting) return;

        if (running)
        {
            LogService.Info("Roblox detected running — status pill switched to RUNNING.");
            return;
        }

        LogService.Info("Roblox closed — back to READY.");

        var found = await Task.Run(() => RobloxLocator.Find(_vm.RobloxPathOverride));
        if (found is not null && found.VersionName != _install?.VersionName)
        {
            _install = found;
            _vm.RobloxVersion = found.VersionName;
            _vm.RobloxPath = found.ExePath;
            LogService.Ok($"New client detected: {found.VersionName}.");
        }
        UpdateStatus();
    }

    private static string ShortVer(RobloxInstall install)
    {
        var name = install.VersionName;
        return name.StartsWith("version-", StringComparison.OrdinalIgnoreCase) && name.Length >= 16
            ? "v" + name.Substring(8, 8)
            : name;
    }

    private async Task UpdateCacheAsync()
    {
        var sizes = await Task.Run(() => (
            Temp: CacheCleaner.SizeString("temp", "logs"),
            Assets: CacheCleaner.SizeString("assets"),
            Download: CacheCleaner.SizeString("download")));

        _vm.CacheSize = sizes.Temp;
        _vm.AssetSize = sizes.Assets;
        _vm.DownloadSize = sizes.Download;
    }

    // ═══════════════════ pages ═══════════════════

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var tag = (sender as RadioButton)?.Tag as string;
        ShowPage(tag switch { "TWEAKS" => 1, "LOG" => 2, _ => 0 });
    }

    private void ShowPage(int index)
    {
        if (_page == index) return;
        _page = index;

        var pages = new[] { PageDash, PageTweaks, PageLog };
        for (var i = 0; i < pages.Length; i++)
            pages[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        var target = pages[index];
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(0.22))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(0.26))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        target.BeginAnimation(UIElement.OpacityProperty, fade);
        ((TranslateTransform)target.RenderTransform).BeginAnimation(TranslateTransform.YProperty, slide);
    }

    // ═══════════════════ busy helper ═══════════════════

    private void SetBusy(bool busy, string? text)
    {
        _vm.IsBusy = busy;
        if (text is not null) _vm.BusyText = text;

        PlayBtn.IsEnabled = !busy;
        OptimizeBtn.IsEnabled = !busy;
        CleanBtn.IsEnabled = !busy;
        RestoreBtn.IsEnabled = !busy;
        ApplyBtn.IsEnabled = !busy;
        RestoreBtn2.IsEnabled = !busy;
    }

    private async Task ResetBusyTextAsync(string last)
    {
        await Task.Delay(3500);
        if (!_busy && _vm.BusyText == last)
            _vm.BusyText = IdleHint;
    }

    // ═══════════════════ pipelines ═══════════════════

    private async void Play_Click(object sender, RoutedEventArgs e) => await ApplyAsync(launch: true);

    private async void Optimize_Click(object sender, RoutedEventArgs e) => await ApplyAsync(launch: false);

    private async void Apply_Click(object sender, RoutedEventArgs e) => await ApplyAsync(launch: false);

    private async Task ApplyAsync(bool launch)
    {
        if (_busy) return;
        _busy = true;
        SetBusy(true, "INITIALIZING…");

        try
        {
            if (RobloxLocator.IsRunning())
            {
                _vm.BusyText = "CLOSING RUNNING ROBLOX…";
                await RobloxLocator.CloseAllAsync();
                LogService.Info("Closed running Roblox client(s) so settings can be rewritten.");
            }

            _vm.BusyText = "DETECTING ROBLOX…";
            _install = await Task.Run(() => RobloxLocator.Find(_vm.RobloxPathOverride));

            _vm.BusyText = $"WRITING {_vm.FlagsCount} FASTFLAGS…";
            var flags = await Task.Run(() => FlagService.Apply(_vm, _install?.Dir));
            if (flags.Files > 0)
                LogService.Ok($"Wrote {flags.Keys} allowlisted FastFlags to {flags.Files} file(s).");
            else
                LogService.Warn("No FastFlag files could be written.");
            foreach (var error in flags.Errors) LogService.Err(error);

            _vm.BusyText = "WRITING LAUNCH SETTINGS…";
            await Task.Run(() =>
            {
                var cap = _vm.FpsCapValue;
                if (SettingsFile.WriteInt("FramerateCap", cap, out var capError))
                    LogService.Info($"FramerateCap set to {(cap < 0 ? "Roblox default" : cap + " FPS")}.");
                else
                    LogService.Warn("FramerateCap: " + capError);

                if (SettingsFile.WriteBool("Fullscreen", _vm.Fullscreen, out var fsError))
                    LogService.Info($"Fullscreen set to {(_vm.Fullscreen ? "on" : "off")}.");
                else
                    LogService.Warn("Fullscreen: " + fsError);
            });

            _vm.BusyText = "TUNING WINDOWS…";
            var tweaks = await Task.Run(() => SystemTweaks.Apply(_vm));
            if (tweaks.Files > 0)
                LogService.Ok($"Applied {tweaks.Files} system change(s).");
            else
                LogService.Info("System settings already match your toggles.");
            foreach (var error in tweaks.Errors) LogService.Warn(error);

            if (launch && _vm.AutoClean)
            {
                _vm.BusyText = "CLEARING TEMP FILES…";
                var freed = await Task.Run(() => CacheCleaner.AutoClean());
                if (freed > 0)
                    LogService.Ok($"Auto-clean freed {CacheCleaner.FormatSize(freed)}.");
            }

            if (launch)
            {
                if (_install is null)
                {
                    LogService.Err("Roblox not found — cannot launch. Press BROWSE on the INSTALL card.");
                    _vm.BusyText = "ROBLOX NOT FOUND";
                    return;
                }

                _vm.BusyText = "LAUNCHING ROBLOX…";
                string launchError = "";
                var launched = await Task.Run(() =>
                    Launcher.Launch(_install!, _vm.TwPriority, out launchError));

                if (launched is null)
                {
                    LogService.Err("Launch failed: " + launchError);
                    _vm.BusyText = "LAUNCH FAILED — SEE LOG";
                }
                else
                {
                    LogService.Ok("Roblox launched with your loadout. GLHF.");
                    _vm.BusyText = "ROBLOX RUNNING — LOADOUT ACTIVE";
                }
            }
            else
            {
                _vm.BusyText = "LOADOUT APPLIED — HIT PLAY WHEN READY";
            }
        }
        catch (Exception ex)
        {
            LogService.Err("Pipeline error: " + ex.Message);
            _vm.BusyText = "ERROR — SEE LOG";
        }
        finally
        {
            UpdateStatus();
            await UpdateCacheAsync();
            SetBusy(false, null);
            _busy = false;
            var last = _vm.BusyText;
            await ResetBusyTextAsync(last);
        }
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        SetBusy(true, "RESTORING STOCK…");

        try
        {
            _install = await Task.Run(() => RobloxLocator.Find(_vm.RobloxPathOverride));

            var flags = await Task.Run(() => FlagService.Restore(_install?.Dir));
            if (flags.Files > 0)
                LogService.Ok($"Removed OVERDRIVE FastFlags from {flags.Files} file(s).");
            else
                LogService.Info("No FastFlag changes to undo.");
            foreach (var error in flags.Errors) LogService.Warn(error);

            var settings = await Task.Run(SettingsFile.Restore);
            if (settings.Keys > 0)
                LogService.Ok($"Restored {settings.Keys} launch setting(s) (frame cap / fullscreen).");
            foreach (var error in settings.Errors) LogService.Warn(error);

            var tweaks = await Task.Run(SystemTweaks.Restore);
            if (tweaks.Files > 0)
                LogService.Ok($"Restored {tweaks.Files} system setting(s).");

            await Task.Run(() =>
            {
                _vm.ApplyProfile(Vm.BalancedProfile);
                SettingsFile.Hydrate(_vm);
                SystemTweaks.Hydrate(_vm);
            });

            _vm.PowerPlan = SystemTweaks.ActivePlanName();
            _vm.BusyText = "STOCK RESTORED";
            LogService.Ok("Everything is back to its pre-OVERDRIVE state.");
        }
        catch (Exception ex)
        {
            LogService.Err("Restore error: " + ex.Message);
            _vm.BusyText = "ERROR — SEE LOG";
        }
        finally
        {
            UpdateStatus();
            await UpdateCacheAsync();
            SetBusy(false, null);
            _busy = false;
            var last = _vm.BusyText;
            await ResetBusyTextAsync(last);
        }
    }

    // ═══════════════════ cache buttons ═══════════════════

    private async void CleanCache_Click(object sender, RoutedEventArgs e) => await CleanAsync("temp");

    private async void CleanAssets_Click(object sender, RoutedEventArgs e) => await CleanAsync("assets");

    private async void CleanDownload_Click(object sender, RoutedEventArgs e) => await CleanAsync("download");

    private async Task CleanAsync(string key)
    {
        if (_busy) return;

        if (key is "assets" or "download" && RobloxLocator.IsRunning())
            LogService.Warn("Roblox is running — locked files will be skipped until you close it.");

        var freed = await Task.Run(() => CacheCleaner.Clean(new[] { key }));
        await UpdateCacheAsync();
        LogService.Info($"Freed {CacheCleaner.FormatSize(freed)} total.");
    }

    // ═══════════════════ browse ═══════════════════

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Locate RobloxPlayerBeta.exe",
            Filter = "RobloxPlayerBeta.exe|RobloxPlayerBeta.exe"
        };

        if (dialog.ShowDialog(this) != true) return;

        _vm.SetRobloxPath(dialog.FileName);
        _install = RobloxLocator.Find(_vm.RobloxPathOverride);
        _vm.RobloxVersion = _install?.VersionName ?? "NOT FOUND";
        _vm.RobloxPath = _install?.ExePath ?? dialog.FileName;
        UpdateStatus();
        LogService.Info("Roblox path set to " + (_install?.ExePath ?? dialog.FileName));
    }

    // ═══════════════════ log buttons ═══════════════════

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _vm.Logs.Clear();
        LogService.Info("Log cleared.");
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine,
            _vm.Logs.Select(l => $"[{l.Time}] {l.Level,-5} {l.Message}"));
        try
        {
            Clipboard.SetText(text);
            LogService.Info("Log copied to clipboard.");
        }
        catch (Exception ex)
        {
            LogService.Warn("Clipboard busy: " + ex.Message);
        }
    }

    // ═══════════════════ window chrome ═══════════════════

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            MaxGlyph.Data = Geometry.Parse("M0.5,2.5 H4.5 V8.5 H0.5 Z M3.5,0.5 H9.5 V6.5 H3.5 Z");
        else if (WindowState == WindowState.Normal)
            MaxGlyph.Data = Geometry.Parse("M0.5,0.5 H9.5 V9.5 H0.5 Z");
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
            return; // clicks on header buttons must not start a drag

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    // maximize to the work area (never cover the taskbar)
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmGetMinMaxInfo = 0x0024;
        if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

        try
        {
            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            var monitor = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor == IntPtr.Zero) return IntPtr.Zero;

            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

            mmi.ptMaxPosition.x = Math.Abs(info.rcWork.left - info.rcMonitor.left);
            mmi.ptMaxPosition.y = Math.Abs(info.rcWork.top - info.rcMonitor.top);
            mmi.ptMaxSize.x = info.rcWork.right - info.rcWork.left;
            mmi.ptMaxSize.y = info.rcWork.bottom - info.rcWork.top;
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        catch { /* fall back to default behaviour */ }

        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointApi { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointApi ptReserved;
        public PointApi ptMaxSize;
        public PointApi ptMaxPosition;
        public PointApi ptMinTrackSize;
        public PointApi ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int left; public int top; public int right; public int bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);
}
