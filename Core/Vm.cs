using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Overdrive.Core;

/// <summary>Single view-model shared by every page. All tuning state is hydrated from
/// what is actually on disk at startup, so the UI always mirrors reality.</summary>
public sealed class Vm : INotifyPropertyChanged
{
    public const int PerfProfile = 0;
    public const int BalancedProfile = 1;
    public const int SmoothProfile = 2;
    public const int CustomProfile = 3;

    public static readonly int[] FpsCaps = { -1, 60, 144, 240, 360 };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static bool Eq<T>(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Eq(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    public readonly AppConfig Config = AppConfig.Load();
    private bool _suspend;

    public Vm() => ApplyProfile(BalancedProfile);

    // ── profile ──────────────────────────────────────────────────────────────

    private int _profile = BalancedProfile;

    public int ProfileIndex
    {
        get => _profile;
        set
        {
            if (value == _profile) return;
            if (value == CustomProfile)
            {
                _profile = CustomProfile;
                Raise();
                Raise(nameof(ProfileName));
                return;
            }
            ApplyProfile(value);
        }
    }

    public string ProfileName => _profile switch
    {
        PerfProfile => "PERFORMANCE",
        BalancedProfile => "BALANCED",
        SmoothProfile => "SMOOTH",
        _ => "CUSTOM"
    };

    /// <summary>Loads a whole loadout at once (used by the profile selector).</summary>
    public void ApplyProfile(int profile)
    {
        if (profile == CustomProfile) return;
        var d = Profiles.Get(profile);

        _suspend = true;
        try
        {
            _quality = d.Quality; Raise(nameof(QualityLevel));
            _texture = d.Texture; Raise(nameof(TextureQuality));
            _msaa = d.Msaa; Raise(nameof(Msaa));
            _api = d.Api; Raise(nameof(ApiMode));
            _crisp = d.Crisp; Raise(nameof(CrispUi));
            _altFix = d.AltFix; Raise(nameof(AltEnterFix));
            _grassMotion = d.GrassMotion; Raise(nameof(GrassMotion));
            _csg0 = d.Csg0; Raise(nameof(Csg0));
            _csg12 = d.Csg12; Raise(nameof(Csg12));
            _csg23 = d.Csg23; Raise(nameof(Csg23));
            _csg34 = d.Csg34; Raise(nameof(Csg34));
            _grassMax = d.GrassMax; Raise(nameof(GrassMax));
            _grassMin = d.GrassMin; Raise(nameof(GrassMin));
        }
        finally { _suspend = false; }

        _profile = profile;
        Raise(nameof(ProfileIndex));
        Raise(nameof(ProfileName));
        RaiseSpecs();
    }

    // ── rendering tuning (FastFlags) ─────────────────────────────────────────

    private int _quality;
    public int QualityLevel { get => _quality; set { if (Set(ref _quality, Math.Clamp(value, 1, 10))) OnTuningChanged(); } }

    private int _texture;
    public int TextureQuality { get => _texture; set { if (Set(ref _texture, Math.Clamp(value, 0, 3))) OnTuningChanged(); } }

    private int _msaa;
    public int Msaa { get => _msaa; set { if (Set(ref _msaa, value)) OnTuningChanged(); } }

    private int _api;
    public int ApiMode { get => _api; set { if (Set(ref _api, Math.Clamp(value, 0, 3))) OnTuningChanged(); } }

    private bool _crisp;
    public bool CrispUi { get => _crisp; set { if (Set(ref _crisp, value)) OnTuningChanged(); } }

    private bool _altFix;
    public bool AltEnterFix { get => _altFix; set { if (Set(ref _altFix, value)) OnTuningChanged(); } }

    private bool _grassMotion;
    public bool GrassMotion { get => _grassMotion; set { if (Set(ref _grassMotion, value)) OnTuningChanged(); } }

    private int _csg0;
    public int Csg0 { get => _csg0; set { if (Set(ref _csg0, value)) OnTuningChanged(); } }

    private int _csg12;
    public int Csg12 { get => _csg12; set { if (Set(ref _csg12, value)) OnTuningChanged(); } }

    private int _csg23;
    public int Csg23 { get => _csg23; set { if (Set(ref _csg23, value)) OnTuningChanged(); } }

    private int _csg34;
    public int Csg34 { get => _csg34; set { if (Set(ref _csg34, value)) OnTuningChanged(); } }

    private int _grassMax;
    public int GrassMax { get => _grassMax; set { if (Set(ref _grassMax, value)) OnTuningChanged(); } }

    private int _grassMin;
    public int GrassMin { get => _grassMin; set { if (Set(ref _grassMin, value)) OnTuningChanged(); } }

    private void OnTuningChanged()
    {
        if (!_suspend)
        {
            var p = FlagService.DetectProfile(this);
            if (p != _profile)
            {
                _profile = p;
                Raise(nameof(ProfileIndex));
                Raise(nameof(ProfileName));
            }
        }
        RaiseSpecs();
    }

    // ── launch settings ──────────────────────────────────────────────────────

    private int _fps;
    public int FpsCapIndex
    {
        get => _fps;
        set
        {
            var clamped = Math.Clamp(value, 0, FpsCaps.Length - 1);
            if (Set(ref _fps, clamped)) RaiseSpecs();
        }
    }

    public int FpsCapValue => FpsCaps[_fps];

    private bool _fullscreen;
    public bool Fullscreen { get => _fullscreen; set => Set(ref _fullscreen, value); }

    // ── system tweaks ────────────────────────────────────────────────────────

    private bool _power;
    public bool TwPower { get => _power; set => Set(ref _power, value); }

    private bool _gameMode = true;
    public bool TwGameMode { get => _gameMode; set => Set(ref _gameMode, value); }

    private bool _dvr = true;
    public bool TwDvr { get => _dvr; set => Set(ref _dvr, value); }

    public bool TwPriority
    {
        get => Config.HighPriority;
        set
        {
            if (Eq(Config.HighPriority, value)) return;
            Config.HighPriority = value;
            Config.Save();
            Raise();
        }
    }

    public bool AutoClean
    {
        get => Config.AutoClean;
        set
        {
            if (Eq(Config.AutoClean, value)) return;
            Config.AutoClean = value;
            Config.Save();
            Raise();
        }
    }

    public string? RobloxPathOverride => Config.RobloxPath;

    public void SetRobloxPath(string? path)
    {
        Config.RobloxPath = path;
        Config.Save();
        Raise(nameof(RobloxPathOverride));
    }

    // ── status / info ────────────────────────────────────────────────────────

    private static readonly Brush ReadyBrush = Frozen("#A3FF12");
    private static readonly Brush WarnBrush = Frozen("#FFB020");
    private static readonly Brush BadBrush = Frozen("#FF4B3E");

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private string _statusText = "SCANNING…";
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

    private Brush _statusBrush = ReadyBrush;
    public Brush StatusBrush { get => _statusBrush; set => Set(ref _statusBrush, value); }

    public void SetStatus(string text, string kind)
    {
        StatusText = text;
        StatusBrush = kind switch { "warn" => WarnBrush, "bad" => BadBrush, _ => ReadyBrush };
    }

    private string _robloxVersion = "…";
    public string RobloxVersion { get => _robloxVersion; set => Set(ref _robloxVersion, value); }

    private string _robloxPath = "scanning…";
    public string RobloxPath { get => _robloxPath; set => Set(ref _robloxPath, value); }

    private string _cacheSize = "…";
    public string CacheSize { get => _cacheSize; set => Set(ref _cacheSize, value); }

    private string _assetSize = "…";
    public string AssetSize { get => _assetSize; set => Set(ref _assetSize, value); }

    private string _downloadSize = "…";
    public string DownloadSize { get => _downloadSize; set => Set(ref _downloadSize, value); }

    private string _powerPlan = "…";
    public string PowerPlan { get => _powerPlan; set => Set(ref _powerPlan, value); }

    // ── busy state ───────────────────────────────────────────────────────────

    private bool _busy;
    public bool IsBusy { get => _busy; set => Set(ref _busy, value); }

    private string _busyText = "Applies your loadout, then launches Roblox";
    public string BusyText { get => _busyText; set => Set(ref _busyText, value); }

    // ── computed specs ───────────────────────────────────────────────────────

    public string SpecQuality => $"{_quality} / 10";
    public string SpecTextures => _texture switch
    {
        0 => "IN-GAME DEFAULT",
        1 => "FORCED LOW · 1",
        2 => "FORCED MEDIUM · 2",
        _ => "FORCED HIGH · 3"
    };
    public string SpecAA => _msaa == 0 ? "OFF" : $"{_msaa}X MSAA";
    public string SpecRenderer => _api switch
    {
        0 => "AUTO (DEFAULT)",
        1 => "DIRECTX 11",
        2 => "VULKAN",
        _ => "OPENGL"
    };
    public string SpecFps => _fps == 0 ? "ROBLOX DEFAULT" : $"{FpsCaps[_fps]} FPS";
    public string SpecGeometry => $"{_csg0} → {_csg34} STUDS";
    public string FlagsCount => FlagService.ManagedCount.ToString();

    private void RaiseSpecs()
    {
        Raise(nameof(SpecQuality));
        Raise(nameof(SpecTextures));
        Raise(nameof(SpecAA));
        Raise(nameof(SpecRenderer));
        Raise(nameof(SpecFps));
        Raise(nameof(SpecGeometry));
        Raise(nameof(FlagsCount));
    }

    // ── log ──────────────────────────────────────────────────────────────────

    public ObservableCollection<LogEntry> Logs { get; } = new();

    // ── hydration ────────────────────────────────────────────────────────────

    /// <summary>Applies a block of disk-sourced values without letting individual
    /// setters flip the profile mid-flight, then re-detects the profile once.</summary>
    public void Hydrate(Action<Vm> apply)
    {
        _suspend = true;
        try { apply(this); }
        finally { _suspend = false; }

        var p = FlagService.DetectProfile(this);
        if (p != _profile)
        {
            _profile = p;
            Raise(nameof(ProfileIndex));
            Raise(nameof(ProfileName));
        }
        RaiseSpecs();
    }
}
