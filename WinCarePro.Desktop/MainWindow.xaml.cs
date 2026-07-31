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
    }

    private void ShowDashboard(object sender, RoutedEventArgs e) => StatusText.Text = "Dashboard ready.";
    private void OpenLegacy(object sender, RoutedEventArgs e) => LaunchLegacy();

    private void LaunchLegacy()
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", "WinCarePro.exe")),
            Path.Combine(AppContext.BaseDirectory, "WinCarePro.exe")
        };
        var exe = Array.Find(candidates, File.Exists);
        if (exe is null)
        {
            StatusText.Text = "The production engine was not found beside this preview.";
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
