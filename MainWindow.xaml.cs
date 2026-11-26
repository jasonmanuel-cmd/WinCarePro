using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WinCareDesktop.ViewModels;

namespace WinCareDesktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _pollTimer;
    private int _pollInFlight;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Preferences affect only the lightweight existing header poll.
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_vm.OptionsVM.Applied.RefreshSeconds) };
        _vm.OptionsVM.AppliedChanged += () => _pollTimer.Interval = TimeSpan.FromSeconds(_vm.OptionsVM.Applied.RefreshSeconds);
        _pollTimer.Tick += async (_, _) =>
        {
            if (Interlocked.Exchange(ref _pollInFlight, 1) == 1) return;
            try
            {
                await _vm.RefreshStatsAsync();
                _vm.DashboardVM.Diagnostics.UpdateFreshness();
            }
            finally
            {
                Interlocked.Exchange(ref _pollInFlight, 0);
            }
        };
        _pollTimer.Start();
        Loaded += async (_, _) =>
        {
            if (_vm.OptionsVM.Applied.DarkDeck)
            {
                Services.ThemePaletteService.ApplyPalette(true, Application.Current?.Resources, Resources);
            }
            _ = _vm.DashboardVM.RefreshPowerSourceAsync();
            await _vm.DashboardVM.Diagnostics.RescanAsync();
        };
        Closed += (_, _) =>
        {
            _pollTimer.Stop();
            _vm.MemoryVM.OnNavigatedFrom();
            _vm.DefenderVM.OnNavigatedFrom();
            _vm.SchedulerVM.Dispose();
            _vm.DiskAnalyzerVM.CancelScan();
            _vm.DashboardVM.Diagnostics.CancelScan();
        };
    }
}
