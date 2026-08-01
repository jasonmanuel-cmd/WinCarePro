using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace WinCarePro.Desktop;

public partial class MainWindow : Window
{
    public ICommand DashboardCommand { get; }
    public ICommand ScanCommand { get; }

    public MainWindow()
    {
        DashboardCommand = new RelayCommand(() => StatusText.Text = "Dashboard ready.");
        ScanCommand = new RelayCommand(LaunchLegacy);
        DataContext = this;
        InitializeComponent();
        UpdatePreviewStatus();
    }

    private void ShowDashboard(object sender, RoutedEventArgs e) => StatusText.Text = "Dashboard ready.";
    private void OpenLegacy(object sender, RoutedEventArgs e) => LaunchLegacy();

    private static string? ResolveLegacyEnginePath()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "dist", "WinCarePro.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "WinCarePro.exe")),
            Path.Combine(AppContext.BaseDirectory, "WinCarePro.exe")
        };

        return Array.Find(candidates, File.Exists);
    }

    private void UpdatePreviewStatus()
    {
        StatusText.Text = ResolveLegacyEnginePath() is null
            ? "Production engine preview not found. Build dist/WinCarePro.exe or run run.bat first."
            : "Production engine preview ready. Open the full engine when you need it.";
    }

    private void LaunchLegacy()
    {
        var exe = ResolveLegacyEnginePath();
        if (exe is null)
        {
            StatusText.Text = "Production engine preview not found. Build dist/WinCarePro.exe or run run.bat first.";
            return;
        }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        StatusText.Text = "Full WinCare Pro opened.";
    }

    private sealed class RelayCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
