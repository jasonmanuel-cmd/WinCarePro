using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Windows.Media;
using WinCareDesktop.Models;
using WinCareDesktop.Services;

namespace WinCareDesktop;

/// <summary>
/// The per-card tool menus.
/// </summary>
/// <remarks>
/// Every dashboard card opens a panel of tools specific to what that card
/// measures, rather than a single canned message. Two rules run through all of
/// it:
///
/// 1. Anything destructive states what it will remove and needs a second,
///    explicit confirmation.
/// 2. Anything whose benefit is overstated on Windows says so. "Free RAM" is
///    mostly theatre on a modern Windows, and a tool that claims otherwise is
///    worse than no tool.
/// </remarks>
public partial class MainWindow
{
    private readonly InstalledProgramsService _programs = new();
    private readonly StorageCleanupService _storage = new();
    private readonly PowerService _power = new();

    // ── Shared chrome ─────────────────────────────────────────────────────

    /// <summary>
    /// A titled panel with a scrolling body. Width is capped so a long tool
    /// list stays readable instead of stretching the dialog.
    /// </summary>
    private FrameworkElement ToolPanel(string intro, params FrameworkElement[] sections)
    {
        var sp = new StackPanel { Width = 640 };

        sp.Children.Add(new TextBlock
        {
            Text = intro,
            FontSize = 12,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        foreach (var s in sections) sp.Children.Add(s);

        return sp;
    }

    private TextBlock PanelHeading(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextPrimary,
        FontFamily = SansFont,
        Margin = new Thickness(0, 0, 0, 8)
    };

    private Border ToolRow(Func<FrameworkElement> build)
    {
        var b = new Border
        {
            Background = SurfaceHover,
            BorderBrush = Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 8)
        };
        b.Child = build();
        return b;
    }

    /// <summary>
    /// A tool row: what it does, what it will touch, and the button that runs
    /// it. <paramref name="result"/> is filled in after the action runs so the
    /// outcome appears on the same row rather than in a separate log the user
    /// has to go and find.
    /// </summary>
    private Border ToolActionRow(string name, string detail, string buttonText,
        Brush accent, Func<System.Threading.Tasks.Task<string>> action,
        bool destructive = false)
    {
        var status = new TextBlock
        {
            Text = "",
            FontSize = 11,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            FontFamily = SansFont
        });
        text.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        text.Children.Add(status);
        grid.Children.Add(text);

        var btn = new Button
        {
            Content = buttonText,
            FontFamily = SansFont,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 8, 14, 8),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            Background = destructive ? Brushes.Transparent : Surface,
            Foreground = destructive ? Warning : accent,
            BorderBrush = destructive ? Warning : Hairline,
            BorderThickness = new Thickness(1)
        };

        btn.Click += async (_, _) =>
        {
            btn.IsEnabled = false;
            btn.Content = "Working…";
            status.Visibility = Visibility.Visible;
            status.Foreground = TextSecondary;
            status.Text = "Running…";

            try
            {
                // Keep the dispatcher alive so the row keeps painting and the
                // user can still cancel with the Close button.
                var message = await action();
                status.Text = message;
                status.Foreground = message.StartsWith('!') ? Warning : Success;
                if (message.StartsWith('!')) status.Text = message.TrimStart('!');
            }
            catch (Exception ex)
            {
                status.Text = "Failed: " + ex.Message;
                status.Foreground = Warning;
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content = buttonText;
            }
        };

        Grid.SetColumn(btn, 1);
        grid.Children.Add(btn);

        return ToolRow(() => grid);
    }

    /// <summary>A read-only fact row, for showing state rather than acting on it.</summary>
    private Border FactRow(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            VerticalAlignment = VerticalAlignment.Center
        });

        var v = new TextBlock
        {
            Text = value,
            FontSize = 12,
            Foreground = TextPrimary,
            FontFamily = MonoFont,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);

        return ToolRow(() => grid);
    }

    /// <summary>
    /// Two-step confirmation for anything that deletes. The destructive button
    /// is hidden until "Review…" is pressed, so a stray click cannot remove data.
    /// </summary>
    private Border ConfirmRow(string name, string detail, string actionText,
        Func<System.Threading.Tasks.Task<string>> action)
    {
        var status = new TextBlock
        {
            Text = "",
            FontSize = 11,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };

        var reviewBtn = MakeFlatButton("Review…", TextSecondary);
        var confirmBtn = MakeFlatButton(actionText, ErrorBrush);
        confirmBtn.Visibility = Visibility.Collapsed;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top
        };
        buttons.Children.Add(reviewBtn);
        buttons.Children.Add(confirmBtn);

        reviewBtn.Click += (_, _) =>
        {
            reviewBtn.Visibility = Visibility.Collapsed;
            confirmBtn.Visibility = Visibility.Visible;
        };

        confirmBtn.Click += async (_, _) =>
        {
            reviewBtn.IsEnabled = false;
            confirmBtn.IsEnabled = false;
            confirmBtn.Content = "Working…";
            status.Visibility = Visibility.Visible;
            status.Text = "Running…";
            status.Foreground = TextSecondary;

            try
            {
                status.Text = await action();
                status.Foreground = Success;
            }
            catch (Exception ex)
            {
                status.Text = "Failed: " + ex.Message;
                status.Foreground = Warning;
            }
            finally
            {
                reviewBtn.IsEnabled = true;
                confirmBtn.IsEnabled = true;
                confirmBtn.Content = actionText;
            }
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            FontFamily = SansFont
        });
        text.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11,
            Foreground = Warning,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        text.Children.Add(status);

        grid.Children.Add(text);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return ToolRow(() => grid);
    }

    /// <summary>A quiet outlined button used by the tool rows.</summary>
    private Button MakeFlatButton(string text, Brush accent)
    {
        var b = new Button
        {
            Content = text,
            FontFamily = SansFont,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(6, 0, 0, 0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Surface,
            Foreground = accent,
            BorderBrush = Hairline,
            BorderThickness = new Thickness(1)
        };
        return b;
    }

    // ── RAM card menu ─────────────────────────────────────────────────────

    /// <summary>
    /// Tools for the RAM card.
    /// </summary>
    /// <remarks>
    /// This panel is deliberately honest about what memory tools can and cannot
    /// do. Trimming a working set moves pages to the standby list; it does not
    /// create RAM, and Windows will page them straight back in under pressure.
    /// The genuinely useful tool here is finding the one process eating the
    /// memory, which is why that list is at the top.
    /// </remarks>
    private FrameworkElement BuildRamToolkit()
    {
        var ram = VM.Service.GetRamStats();
        var standby = VM.Service.GetStandbyMemoryBytes();

        var facts = new StackPanel();
        facts.Children.Add(PanelHeading("Right now"));
        facts.Children.Add(FactRow("Physical memory",
            $"{FormatBytes(ram.totalBytes - ram.availableBytes)} used of " +
            $"{FormatBytes(ram.totalBytes)} ({ram.usedPercent:0}%)"));
        facts.Children.Add(FactRow("Available to apps", FormatBytes(ram.availableBytes)));
        facts.Children.Add(FactRow("Standby list",
            standby < 0 ? "not reported by this system" : FormatBytes(standby)));

        IReadOnlyList<ProcessInfo> topProcs = VM.TopProcesses.Count > 0
            ? VM.TopProcesses.ToList()
            : VM.Service.GetTopMemoryProcesses(6);

        var procs = new StackPanel();
        procs.Children.Add(PanelHeading("Heaviest processes"));
        procs.Children.Add(new TextBlock
        {
            Text = "Trimming a working set asks a program to give up the memory it is " +
                   "holding but not using. It can help when something is paging, and " +
                   "will not add memory to the machine.",
            FontSize = 11,
            Foreground = TextTertiary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        if (topProcs.Count == 0)
        {
            procs.Children.Add(new TextBlock
            {
                Text = "No process information available.",
                FontSize = 12,
                Foreground = TextSecondary,
                FontFamily = SansFont
            });
        }

        foreach (var p in topProcs)
        {
            var proc = p;
            procs.Children.Add(ToolActionRow(
                $"{proc.Name}  (pid {proc.Pid})",
                $"Using {proc.WorkingSetMb * 1024:F0} MB right now.",
                "Trim",
                Primary,
                () => Task.Run(() =>
                {
                    var ok = VM.Service.TrimProcessWorkingSet(proc.Pid);
                    return ok
                        ? $"Trimmed {proc.Name}'s working set. It will fault pages back in as it needs them."
                        : $"!Could not trim {proc.Name} — it may be protected or already exited.";
                })));
        }

        var tools = new StackPanel();
        tools.Children.Add(PanelHeading("System tools"));
        tools.Children.Add(ToolActionRow(
            "Empty the standby memory list",
            "Runs EmptyStandbyList, which drops cached pages the OS was holding in " +
            "reserve. Needs Administrator. Reduces the number shown as 'cached'.",
            "Flush",
            Primary,
            () => Task.Run(() =>
            {
                if (NativeMethods.TryEmptyStandbyList())
                {
                    var after = VM.Service.GetStandbyMemoryBytes();
                    return after >= 0
                        ? $"Standby list emptied. It now holds {FormatBytes(after)}."
                        : "Standby list emptied.";
                }
                return "!EmptyStandbyList was unavailable. Run WinCare as administrator to use it.";
            })));

        tools.Children.Add(ToolActionRow(
            "Flush the DNS resolver cache",
            "Clears cached name lookups. Useful after switching DNS if names still " +
            "resolve to the old server. Costs a few seconds on the next lookup.",
            "Flush",
            Primary,
            () => Task.Run(() =>
            {
                VM.Service.FlushDnsCache();
                return "DNS resolver cache flushed.";
            })));

        return ToolPanel(
            "Memory tools. Nothing here fabricates free memory — Windows manages " +
            "physical pages on its own, and the useful lever is usually finding the " +
            "one program holding too much.",
            facts, procs, tools);
    }

    // ── Storage card menu ─────────────────────────────────────────────────

    private FrameworkElement BuildStorageToolkit()
    {
        var disk = VM.Service.GetStorageStats();

        var facts = new StackPanel();
        facts.Children.Add(PanelHeading("Right now"));
        facts.Children.Add(FactRow("System drive",
            $"{disk.usedGb:0.#} GB used of {disk.totalGb:0.#} GB ({disk.usedPercent:0}%)"));
        facts.Children.Add(FactRow("Free space", $"{disk.totalGb - disk.usedGb:0.#} GB"));

        var reclaim = new StackPanel();
        reclaim.Children.Add(PanelHeading("Reclaim space"));
        reclaim.Children.Add(new TextBlock
        {
            Text = "Every tool below deletes something Windows will not hand back. " +
                   "Each one is hidden behind a second confirmation.",
            FontSize = 11,
            Foreground = Warning,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        reclaim.Children.Add(ToolActionRow(
            "Temporary files",
            "Scans temp folders and browser caches, then shows you exactly what it " +
            "would delete before deleting anything.",
            "Preview",
            Primary,
            async () =>
            {
                ShowSub("Storage tools", "Scanning temp folders…");
                var candidates = await Task.Run(() => VM.Service.ScanTempForCleanup());
                ShowSub("Storage tools", BuildMaintenancePreviewContent(candidates));
                return $"{candidates.Count} reclaimable item(s) found. Review the preview.";
            }));

        reclaim.Children.Add(ConfirmRow(
            "Empty the Recycle Bin",
            "Permanently deletes everything currently in the Recycle Bin on all drives.",
            "Empty it",
            () => Task.Run(() =>
            {
                var removed = _storage.EmptyRecycleBin();
                if (removed < 0) return "!Windows would not empty the Recycle Bin.";
                var now = _storage.GetRecycleBinSize();
                return removed == 0
                    ? "The Recycle Bin was already empty."
                    : $"Emptied {removed} item(s). It now holds {FormatBytes(now)}.";
            })));

        reclaim.Children.Add(ConfirmRow(
            "Windows Update download cache",
            "Deletes staged update downloads so a pending update can be re-fetched. " +
            "Needs Administrator. Saves bandwidth later, not update time.",
            "Clear it",
            () => Task.Run(() =>
            {
                var freed = _storage.ClearWindowsUpdateCache();
                if (freed < 0) return "!Could not clear the cache. Run as administrator.";
                return freed == 0
                    ? "The cache was already empty."
                    : $"Reclaimed {FormatBytes(freed)} of staged update downloads.";
            })));

        reclaim.Children.Add(ConfirmRow(
            "DISM component cleanup",
            "Runs DISM's component cleanup, which reclaims superseded Windows update " +
            "components. Safe and genuinely useful, but it takes several minutes and " +
            "cannot be interrupted.",
            "Run it",
            () => Task.Run(() =>
            {
                var (ok, output) = _storage.RunDismComponentCleanup();
                if (!ok) return "!DISM reported a failure:\n" + Truncate(output, 400);
                var line = output.Split('\n').LastOrDefault(l => l.Trim().Length > 0)?.Trim();
                return "DISM component cleanup finished. " + (line ?? "");
            })));

        return ToolPanel(
            "Storage tools. Deletions here are permanent, so each one states exactly " +
            "what it removes and waits for a second confirmation.",
            facts, reclaim);
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";

    // ── Power card menu ───────────────────────────────────────────────────

    /// <summary>Battery level as shown on the Power card, or empty when unknown.</summary>
    private string PowerBatteryText() => $"{VM.BatteryPercent:0}%";

    private FrameworkElement BuildPowerToolkit()
    {
        var onBattery = _power.IsOnBattery();

        var facts = new StackPanel();
        facts.Children.Add(PanelHeading("Right now"));
        facts.Children.Add(FactRow("Power source",
            onBattery ? "Running on battery" : "Running on mains power"));
        facts.Children.Add(FactRow("Battery level",
            _hasBattery == true ? PowerBatteryText() : "no battery detected"));

        var plans = new StackPanel();
        plans.Children.Add(PanelHeading("Power plan"));
        plans.Children.Add(new TextBlock
        {
            Text = "Switching the plan changes how aggressively the CPU boosts and how " +
                   "quickly it idles. It is reversible and recorded in Undo History.",
            FontSize = 11,
            Foreground = TextTertiary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        foreach (var plan in _power.GetPowerPlans())
        {
            var p = plan;
            plans.Children.Add(ToolActionRow(
                p.Name + (p.Active ? "  (active)" : ""),
                p.Active ? "Currently applied." : "Switch the system to this plan.",
                p.Active ? "Active" : "Use it",
                Primary,
                () => Task.Run(() =>
                {
                    var (ok, message) = _power.SetPowerPlan(p.Guid);
                    if (!ok) return "!" + message;

                    var previous = _power.GetPowerPlans().FirstOrDefault(x => x.Active)?.Name ?? "";
                    VM.Service.PushUndoTransaction(new OptimizationTransaction
                    {
                        Title = $"Power plan changed to {p.Name}",
                        Description = $"Previously: {(previous.Length == 0 ? "unknown" : previous)}",
                        Kind = UndoKind.DisplayOnly
                    });
                    ShowSub("Power tools", BuildPowerToolkit());
                    return $"Power plan switched to {p.Name}.";
                }),
                destructive: false));
        }

        return ToolPanel(
            "Power tools.",
            facts, plans);
    }

    // ── Uninstall Programs card ───────────────────────────────────────────

    private string _programFilter = "";
    private List<InstalledProgram> _programCache = new();

    private FrameworkElement BuildUninstallPrograms()
    {
        var sp = new StackPanel { Width = 700 };

        sp.Children.Add(new TextBlock
        {
            Text = "Every program Windows has registered an uninstaller for, largest first. " +
                   "WinCare runs the vendor's own uninstaller and never deletes a program's " +
                   "folder, because that leaves registry keys and services behind and breaks " +
                   "uninstall and repair.",
            FontSize = 12,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var search = new TextBox
        {
            Text = _programFilter,
            FontFamily = SansFont,
            FontSize = 13,
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = TextTertiary,
            BorderThickness = new Thickness(1),
            Background = SurfaceHover
        };

        // A TextBox has no placeholder, so an empty search field reads as broken.
        // Overlay a hint that disappears as soon as there is a filter.
        var hint = new TextBlock
        {
            Text = "Search by name or publisher…",
            FontSize = 13,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            IsHitTestVisible = false,
            Margin = new Thickness(11, 9, 0, 0)
        };
        // The host needs an explicit height. Inside a vertical StackPanel the
        // Grid measured the TextBox at zero because the overlaid hint carried no
        // height of its own, leaving an invisible sliver where the search box
        // should be.
        var searchHost = new Grid { MinHeight = 36 };
        searchHost.Children.Add(search);
        searchHost.Children.Add(hint);
        hint.Visibility = _programFilter.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        search.TextChanged += (_, _) => hint.Visibility =
            search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        sp.Children.Add(searchHost);

        var summary = new TextBlock
        {
            FontSize = 11,
            Foreground = TextTertiary,
            FontFamily = SansFont,
            Margin = new Thickness(0, 8, 0, 10)
        };
        sp.Children.Add(summary);

        var listHost = new StackPanel();
        sp.Children.Add(listHost);

        search.TextChanged += (_, _) =>
        {
            _programFilter = search.Text;
            RefreshProgramList(listHost, summary);
        };

        // Enumerating every uninstall key touches dozens of registry hives, so it
        // must not run on the UI thread.
        _ = Task.Run(() => _programs.GetInstalledPrograms())
                 .ContinueWith(t =>
                 {
                     if (t.IsFaulted) return;
                     _programCache = t.Result;
                     Dispatcher.Invoke(() => RefreshProgramList(listHost, summary));
                 });

        return sp;
    }

    private void RefreshProgramList(StackPanel host, TextBlock summary)
    {
        host.Children.Clear();

        var filter = _programFilter.Trim();
        var matches = filter.Length == 0
            ? _programCache
            : _programCache.Where(p =>
                p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.Publisher.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        var known = matches.Where(p => p.SizeKnown).Sum(p => p.SizeBytes);
        summary.Text = matches.Count == _programCache.Count
            ? $"{matches.Count} programs · {_programCache.Where(p => p.SizeKnown).Sum(p => p.SizeBytes) / 1073741824.0:0.0} GB of known installs"
            : $"{matches.Count} of {_programCache.Count} programs · {known / 1073741824.0:0.0} GB of known installs";

        if (matches.Count == 0)
        {
            host.Children.Add(new TextBlock
            {
                Text = _programCache.Count == 0
                    ? "Reading the uninstall registry…"
                    : "Nothing matches that search.",
                FontSize = 12,
                Foreground = TextSecondary,
                FontFamily = SansFont
            });
            return;
        }

        foreach (var program in matches.Take(200))
            host.Children.Add(BuildProgramRow(program));
    }

    private Border BuildProgramRow(InstalledProgram program)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock
        {
            Text = program.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            FontFamily = SansFont,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var detail = new List<string> { program.DisplaySize };
        if (program.Publisher.Length > 0) detail.Add(program.Publisher);
        if (program.Version.Length > 0) detail.Add("v" + program.Version);
        if (program.InstallDate.Length > 0) detail.Add("installed " + program.InstallDate);

        text.Children.Add(new TextBlock
        {
            Text = string.Join("  ·  ", detail),
            FontSize = 11,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        grid.Children.Add(text);

        var uninstall = MakeFlatButton("Uninstall", ErrorBrush);
        uninstall.Margin = new Thickness(6, 0, 0, 0);
        uninstall.Click += (_, _) => ConfirmUninstall(program);
        Grid.SetColumn(uninstall, 1);
        grid.Children.Add(uninstall);

        return ToolRow(() => grid);
    }

    /// <summary>
    /// Two-step uninstall. The second panel spells out exactly what will run
    /// before anything is launched.
    /// </summary>
    private void ConfirmUninstall(InstalledProgram program)
    {
        if (!InstalledProgramsService.TryBuildUninstallCommand(
                program, out var file, out var args, out var warning))
        {
            ShowSub("Uninstall programs",
                $"WinCare cannot remove {program.Name} automatically. {warning}");
            return;
        }

        var sp = new StackPanel { Width = 640 };

        sp.Children.Add(new TextBlock
        {
            Text = program.Name,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });

        sp.Children.Add(new TextBlock
        {
            Text = "This will run the program's own uninstaller:",
            FontSize = 12,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            Margin = new Thickness(0, 0, 0, 6)
        });

        sp.Children.Add(new Border
        {
            Background = SurfaceHover,
            BorderBrush = Hairline,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 14),
            Child = new TextBlock
            {
                Text = args.Length > 0 ? $"{file} {args}" : file,
                FontSize = 11,
                Foreground = TextPrimary,
                FontFamily = MonoFont,
                TextWrapping = TextWrapping.Wrap
            }
        });

        if (warning != null)
        {
            sp.Children.Add(new TextBlock
            {
                Text = warning,
                FontSize = 12,
                Foreground = Warning,
                FontFamily = SansFont,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });
        }

        sp.Children.Add(new TextBlock
        {
            Text = "Uninstalling can remove your settings and data for this program, and " +
                   "WinCare cannot undo it. Programs other than Store apps are usually " +
                   "better removed this way than by deleting their folder.",
            FontSize = 12,
            Foreground = Warning,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var run = MakeFlatButton("Run the uninstaller", ErrorBrush);
        run.Click += (_, _) =>
        {
            run.IsEnabled = false;
            run.Content = "Launching…";
            try
            {
                var (code, _, stderr) = VM.Service.RunProcessPublic(file, args, 600_000);

                VM.Service.PushUndoTransaction(new OptimizationTransaction
                {
                    Title = $"Uninstalled {program.Name}",
                    Description = $"Ran {Path.GetFileName(file)}. " +
                                  (code == 0
                                      ? "Completed."
                                      : $"Exited with code {code}. {Truncate(stderr, 200)}"),
                    // Reinstalling an arbitrary program is not something WinCare
                    // can do safely, so this is a record, not a rollback.
                    Kind = UndoKind.DisplayOnly
                });

                // The registry has changed; refresh the list behind this dialog.
                _programCache = new List<InstalledProgram>();
                ShowSub("Uninstall programs",
                    code == 0
                        ? $"{program.Name} was uninstalled. It is recorded in Undo History."
                        : $"The uninstaller for {program.Name} exited with code {code}. " +
                          "It may still be running, or may have been declined.");
            }
            catch (Exception ex)
            {
                ShowSub("Uninstall programs", "Could not launch the uninstaller: " + ex.Message);
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        buttons.Children.Add(run);
        sp.Children.Add(buttons);

        ShowSub($"Uninstall {program.Name}", sp);
    }

    // ── Card entry points ────────────────────────────────────────────────

    /// <summary>
    /// Opens a card's tool menu, refreshing live numbers first so the panel
    /// never shows stale readings.
    /// </summary>
    private void ShowRamToolkit()
    {
        VM.Service.GetRamStats();
        ShowSub("RAM tools", BuildRamToolkit());
    }

    private void ShowStorageToolkit()
    {
        ShowSub("Storage tools", BuildStorageToolkit());
    }

    private void ShowPowerToolkit()
    {
        ShowSub("Power tools", BuildPowerToolkit());
    }

    private void UninstallPrograms_Click(object s, MouseButtonEventArgs e)
        => ShowSub("Uninstall programs", BuildUninstallPrograms());
}
