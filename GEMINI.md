# WinCare Pro — Project Guidelines & Architecture

## Overview
WinCare Pro is a native Windows 10/11 system optimizer, privacy protector, and disk analyzer built in C# .NET 8 (WPF).
The core design pillars are:
- **100% Offline & Zero Telemetry:** No background tracking or phone-home requests.
- **Single-File Self-Contained:** Runs without external runtime requirements (`win-x64`).
- **Complete Reversibility:** Every action logs an undo transaction in `HistoryStore`.

## Architecture (v2.0)
- **MVVM Pattern:** Driven by `CommunityToolkit.Mvvm`.
- **Navigation Shell:** `MainWindow.xaml` hosts a left navigation sidebar with dynamic view switching via `ContentControl` bound to `CurrentView`.
- **Views:** Dedicated XAML UserControls in `Views/`:
  - `DashboardView`: Live hardware telemetry, quick optimize, health score.
  - `DiskAnalyzerView`: Deep recursive directory scanner with visual size percentages.
  - `StartupManagerView`: Boot application management with impact ratings.
  - `AppUninstallView`: Interactive multi-select uninstaller for Win32 and Windows Store apps.
  - `PrivacyView`: Toggles for Windows diagnostic data and telemetry.
  - `NetworkMonitorView`: Interface throughput, ping tester, DNS flush & switcher.
  - `SchedulerView`: Automation preferences for scheduled tune-ups.
  - `HistoryLogsView`: Undo transaction manager and live event log.
- **Services:** Core system management in `Services/` (`SystemOptimizerService`, `StorageCleanupService`, `AppxService`, `DiskAnalyzerService`, `StartupManagerService`, `NetworkMonitorService`, `SchedulerService`, `HistoryStore`).

## Verification & Build Commands
- Run test suite: `dotnet test WinCareDesktop.Tests/WinCareDesktop.Tests.csproj`
- Build Debug: `dotnet build WinCareDesktop.csproj -c Debug`
- Build Release single-file: `powershell -ExecutionPolicy Bypass -File .\build-release.ps1`
- Build MSI Installer: `powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -SkipTests`
