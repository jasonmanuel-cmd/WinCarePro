using System.Windows;
using System.Windows.Threading;
using WinCareDesktop.ViewModels;

namespace WinCareDesktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _pollTimer;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Background poll for system telemetry every 5 seconds
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += async (_, _) =>
        {
            await Task.Run(() => _vm.RefreshStats());
        };
        _pollTimer.Start();
    }
}
