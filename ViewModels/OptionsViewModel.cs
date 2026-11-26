using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using WinCareDesktop.Services;

namespace WinCareDesktop.ViewModels;

public partial class OptionsViewModel : ViewModelBase
{
    private readonly RescueOptionsStore _store;
    private readonly LicenseService _licenseService;

    public OptionsViewModel(RescueOptionsStore? store = null, LicenseService? licenseService = null)
    {
        _store = store ?? new(Path.Combine(SafetyMode.DataRoot, "options.json"));
        _licenseService = licenseService ?? new LicenseService();
        Title = "Options";
        Subtitle = "App preferences & offline license • safety confirmations stay required";
        var loaded = _store.Load();
        Apply(loaded.Value);
        Status = loaded.Message;
        RefreshLicenseState();
    }

    public int[] RefreshIntervals { get; } = [5, 15, 30, 60];

    [ObservableProperty] private int _refreshSeconds;
    [ObservableProperty] private bool _localDiagnostics;
    [ObservableProperty] private bool _darkDeck;
    [ObservableProperty] private string _status = "";

    [ObservableProperty] private string _licenseKeyInput = "";
    [ObservableProperty] private string _licenseStatus = "";
    [ObservableProperty] private bool _isLicensed;
    [ObservableProperty] private string _editionBadge = "v2.0 PRO";

    public RescueOptions Applied { get; private set; } = new();
    public event Action? AppliedChanged;
    public event Action? LicenseChanged;

    public bool CanSave => !SafetyMode.Enabled;

    partial void OnDarkDeckChanged(bool value) => ThemePaletteService.ApplyToCurrentApp(value);

    private void Apply(RescueOptions value)
    {
        Applied = value;
        RefreshSeconds = value.RefreshSeconds;
        LocalDiagnostics = value.LocalDiagnostics;
        DarkDeck = value.DarkDeck;
        if (value.DarkDeck) ThemePaletteService.ApplyToCurrentApp(true);
        AppliedChanged?.Invoke();
    }

    public void RefreshLicenseState()
    {
        var current = _licenseService.LoadActivatedLicense();
        IsLicensed = current.IsValid;
        LicenseStatus = current.Message;
        EditionBadge = current.IsValid ? "v2.0 PRO" : "v2.0 BETA";
        LicenseChanged?.Invoke();
    }

    [RelayCommand]
    public void ActivateLicense()
    {
        var result = _licenseService.ActivateAndSave(LicenseKeyInput);
        IsLicensed = result.IsValid;
        LicenseStatus = result.Message;
        if (result.IsValid)
        {
            LicenseKeyInput = "";
            EditionBadge = "v2.0 PRO";
        }
        LicenseChanged?.Invoke();
    }

    [RelayCommand]
    public void DeactivateLicense()
    {
        var (ok, message) = _licenseService.Deactivate();
        if (ok)
        {
            IsLicensed = false;
            EditionBadge = "v2.0 BETA";
        }
        LicenseStatus = message;
        LicenseChanged?.Invoke();
    }

    [RelayCommand]
    public void Save() => Persist(new(RefreshSeconds: RefreshSeconds, ReducedMotion: true, LocalDiagnostics: LocalDiagnostics, DarkDeck: DarkDeck));

    [RelayCommand]
    public void Reset() => Persist(new());

    private void Persist(RescueOptions value)
    {
        if (SafetyMode.Enabled)
        {
            Status = "Saving app preferences is disabled in safe mode.";
            return;
        }
        try
        {
            _store.Save(value);
            Apply(value);
            Status = "App preferences saved. Scheduler, Windows settings and confirmations are unchanged. Diagnostic preference applies to future logs; old files are retained.";
        }
        catch (Exception)
        {
            Status = "Preferences could not be saved. Previously applied preferences remain in use; check permissions and retry.";
        }
    }
}
