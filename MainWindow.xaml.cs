using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinCareDesktop.Models;
using WinCareDesktop.ViewModels;

namespace WinCareDesktop;

public partial class MainWindow : Window
{
    // reusable labels (must be fields so event handlers can reach them)
    private readonly TextBlock _clockDisplay = new();
    private readonly TextBlock _ramSub = new(), _storageSub = new(), _powerSub = new();
    private readonly TextBlock _ramVal = new(), _storageVal = new(), _powerVal = new();
        private static string HistoryStorePathHint =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "WinCarePro", "history.json");

    private DispatcherTimer? _pollTimer;
    private DispatcherTimer? _clockTimer;
    private TextBlock? _headerBattery;
    private TextBlock? _statusLabel;
    private TextBlock? _dnsActiveLabel;
    private TextBlock? _scoreLabelBig;
    private Ellipse? _scoreRing;
    private StackPanel? _modulePanel;
    private ListBox? _logList;
    private Button? _optimizeButton;
    private double _ringCircumference;
    private bool _polling;
    private bool _batteryChecked;
    private bool? _hasBattery;

public MainWindow()
    {
        InitializeComponent();
        try
        {
            DataContext = new MainViewModel();

            // Regions are declared in MainWindow.xaml; populate them here.
            BuildHeader();
            BuildScoreCard();
            BuildActionRow();
            BuildTelemetryRow();
            BuildModuleList();
            BuildDnsPanel();
            BuildLogPanel();
            WireVmEvents();

            UpdateClock();
            StartTimers();

            // Prime the battery probe before the first paint. UpdateLiveCards
            // renders "Detecting..." whenever _hasBattery is null, and nothing
            // else called HasBattery(), so the Power card sat on that
            // placeholder forever unless the user happened to open the Power
            // tools panel. The probe itself is a WMI round-trip, so it returns
            // immediately and completes on a background thread.
            _ = HasBattery();

            UpdateLiveCards();
        }
        catch (Exception ex)
        {
            // The log directory may not exist yet, so create it before writing.
            try
            {
                var dir = System.IO.Path.GetDirectoryName(CrashLogPath());
                if (dir != null) Directory.CreateDirectory(dir);
                File.WriteAllText(CrashLogPath(),
                    "CRASH in constructor: " + ex + Environment.NewLine + ex.StackTrace);
            }
            catch { /* never let logging mask the original failure */ }
            throw;
        }
    }

    private void StartTimers()
    {
        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        clock.Tick += (_, _) => UpdateClock();
        clock.Start();
        _clockTimer = clock;

        // Poll telemetry off the UI thread. WMI queries take 100-400ms each,
        // so running them on the dispatcher froze the window.
        var poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        poll.Tick += (_, _) =>
        {
            if (_polling) return;
            _polling = true;

            // Capture VM here, on the UI thread. This poll runs every 5s, so
            // dereferencing VM inside the lambda would throw on every tick.
            var vm = VM;
            Task.Run(() =>
            {
                try { vm.RefreshStats(); }
                catch { /* a failed poll must not kill the timer */ }
                finally { Dispatcher.Invoke(() => _polling = false); }
            });
        };
        poll.Start();
        _pollTimer = poll;
    }

    /// <summary>
    /// Crash log path, matching the one App writes to.
    /// </summary>
    /// <remarks>
    /// Unused since crash handling moved into App, but the Diagnostics panel
    /// offers to open this file, so the path has to stay in one place the two
    /// agree on.
    /// </remarks>
    private static string CrashLogPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinCarePro", "crash.log");
// ── Theme access ─────────────────────────────────────────────────────────
    // These were static readonly fields with hard-coded RGB values, duplicated
    // from Themes/Theme.xaml. They are now resolved through the merged
    // dictionary so there is exactly one source of truth for the palette.
    private object Theme(string key) =>
        Application.Current?.TryFindResource(key) ?? FindResource(key);

    private Brush Brush2(string key) => (Brush)Theme(key);

    private Brush BgBrush => Brush2("BgBrush");
    private Brush Surface => Brush2("Surface");
    private Brush SurfaceHover => Brush2("SurfaceHover");
    private Brush Primary => Brush2("Primary");
    private Brush PrimaryDark => Brush2("PrimaryDark");
    private Brush PrimaryLight => Brush2("PrimaryLight");
    private Brush TextPrimary => Brush2("TextPrimary");
    private Brush TextSecondary => Brush2("TextSecondary");
    private Brush TextTertiary => Brush2("TextTertiary");
    private Brush Success => Brush2("Success");
    private Brush Warning => Brush2("Warning");
    private Brush ErrorBrush => Brush2("Error");
    private Brush Hairline => Brush2("Hairline");
    private FontFamily SansFont => (FontFamily)Theme("SansFont");
    private FontFamily MonoFont => (FontFamily)Theme("MonoFont");
    private FontFamily EmojiFont => (FontFamily)Theme("EmojiFont");
    private Color PrimaryColor => ((SolidColorBrush)Primary).Color;

    // VM is reached via DataContext, so this getter is only valid on the UI thread.
    // Anything handed to Task.Run must capture it into a local first.
    private MainViewModel VM => (MainViewModel)DataContext;

    // ── Region builders ──────────────────────────────────────────────────
    // Each region is declared in MainWindow.xaml and populated here. Splitting
    // the old 490-line BuildUI() into these makes each one independently
    // testable and keeps the styling in Themes/Theme.xaml.

    // XAML x:Name already generates these as fields; MainColumn, Sidebar and
    // SubScreenOverlay shadow the generated names because this file is
    // MainWindow.xaml's code-behind, so they are referenced directly.

    private void BuildHeader()
    {
        BrandPanel.Children.Clear();
        BrandPanel.Children.Add(new TextBlock
        {
            Text = "⚡",
            FontSize = 22,
            FontFamily = EmojiFont,
            Foreground = Primary,
            Margin = new Thickness(0, 0, 10, 0)
        });
        BrandPanel.Children.Add(new TextBlock
        {
            Text = "WinCare Pro",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center
        });

        var adminBtn = new Button
        {
            Content = Services.SystemOptimizerService.IsElevated() ? "Administrator" : "Run as administrator",
            Margin = new Thickness(16, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11
        };
        adminBtn.Click += (_, _) => RelaunchElevated();
        BrandPanel.Children.Add(adminBtn);

        HeaderRightPanel.Children.Clear();
        _clockDisplay.Text = DateTime.Now.ToString("hh:mm tt");
        _clockDisplay.FontSize = 14;
        _clockDisplay.Foreground = TextSecondary;
        _clockDisplay.FontFamily = MonoFont;
        _clockDisplay.Margin = new Thickness(0, 0, 16, 0);
        _clockDisplay.VerticalAlignment = VerticalAlignment.Center;
        HeaderRightPanel.Children.Add(_clockDisplay);

        HeaderRightPanel.Children.Add(new TextBlock
        {
            Text = "\U0001F50B",
            FontSize = 14,
            FontFamily = EmojiFont,
            Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center
        });

        _headerBattery = new TextBlock
        {
            FontSize = 14,
            Foreground = TextPrimary,
            FontWeight = FontWeights.Medium,
            FontFamily = MonoFont,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        HeaderRightPanel.Children.Add(_headerBattery);
    }

    private void BuildScoreCard()
    {
        var card = new Border
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(0, 0, 0, 12),
            // Tighter than the old 24,20,24,20. Combined with the smaller ring
            // this is what buys back the vertical space for the card list.
            Padding = new Thickness(24, 14, 24, 14)
        };

        // Declared before first use: the column width below is sized from it.
        const int RingSize = 88;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RingSize) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── Score ring ──
        // Circumference of the ring (r = size/2) minus half the stroke on each
        // side, so the arc maths matches what is actually painted.
        //
        // The ring is 88px rather than the original 120px host. At 720px window
        // height the hero pushed everything below it off-screen: only two and a
        // half of the module cards were reachable without scrolling. The score
        // is legible at this size and the space goes to the tools instead.
        const int RingStroke = 9;
        var ringDiameter = RingSize - (RingStroke * 2);

        _ringCircumference = 2 * Math.PI * (ringDiameter / 2.0 - RingStroke / 2.0);

        var ringCanvas = new Canvas { Width = RingSize, Height = RingSize };
        var track = new Ellipse
        {
            Width = ringDiameter,
            Height = ringDiameter,
            Stroke = Hairline,
            StrokeThickness = RingStroke,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(track, RingStroke);
        Canvas.SetTop(track, RingStroke);
        ringCanvas.Children.Add(track);

        _scoreRing = new Ellipse
        {
            Width = ringDiameter,
            Height = ringDiameter,
            Stroke = Primary,
            StrokeThickness = RingStroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeDashCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            StrokeDashArray = new DoubleCollection([0, _ringCircumference]),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-90),
            Effect = new DropShadowEffect { Color = PrimaryColor, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.18 }
        };
        Canvas.SetLeft(_scoreRing, RingStroke);
        Canvas.SetTop(_scoreRing, RingStroke);
        ringCanvas.Children.Add(_scoreRing);

        _scoreLabelBig = new TextBlock
        {
            Text = "—",
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = Primary,
            FontFamily = MonoFont,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var ringHost = new Grid { Width = RingSize, Height = RingSize };
        ringHost.Children.Add(ringCanvas);
        ringHost.Children.Add(_scoreLabelBig);
        grid.Children.Add(ringHost);

        // ── Score copy ──
        var copy = new StackPanel { Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock
        {
            Text = "System Health Score",
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = TextSecondary
        });
        copy.Children.Add(new TextBlock
        {
            Text = "out of 100",
            FontSize = 11,
            Foreground = TextTertiary,
            FontFamily = MonoFont,
            Margin = new Thickness(0, 4, 0, 4)
        });
        copy.Children.Add(new TextBlock
        {
            Text = "RAM headroom, free disk, and privacy shields, weighted from this PC's live readings.",
            FontSize = 12,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);

        card.Child = grid;
        MainColumn.Children.Add(card);
        Grid.SetRow(card, 0);
    }

    private void BuildActionRow()
    {
        var optBtn = new Button
        {
            Content = "⚡  Run System Maintenance",
            Style = (Style)FindResource("PrimaryButton"),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        optBtn.Click += (_, _) => OnOptimizeClick();
        _optimizeButton = optBtn;

        // StatusMessage and IsOptimizing existed on the VM but nothing showed
        // them, so the button gave no feedback during a multi-second run.
        _statusLabel = new TextBlock
        {
            Text = VM.StatusMessage,
            FontSize = 12,
            Foreground = TextSecondary,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 460
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 20)
        };
        row.Children.Add(optBtn);
        row.Children.Add(_statusLabel);

        MainColumn.Children.Add(row);
        Grid.SetRow(row, 1);
    }

    private void BuildTelemetryRow()
    {
        var wrap = new Grid();
        wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        wrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var card0 = MakeTelCard("\U0001F4C2", "RAM Usage", _ramVal, _ramSub, RamCard_Click);
        var card1 = MakeTelCard("\U0001F4A1", "Storage", _storageVal, _storageSub, StorageCard_Click);
        var card2 = MakeTelCard("\U0001F50B", "Power", _powerVal, _powerSub, PowerCard_Click);
        wrap.Children.Add(card0); Grid.SetColumn(card0, 0);
        wrap.Children.Add(card1); Grid.SetColumn(card1, 1);
        wrap.Children.Add(card2); Grid.SetColumn(card2, 2);

        // Telemetry cards and the module list share row 2, so they need their
        // own stacked rows. Adding both to one bare Grid put them in the same
        // cell, which collapsed the cards behind the module list.
        var host = new Grid();
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        host.Children.Add(wrap);
        Grid.SetRow(wrap, 0);

        var modules = BuildModuleListHost();
        host.Children.Add(modules);
        Grid.SetRow(modules, 1);

        MainColumn.Children.Add(host);
        Grid.SetRow(host, 2);
    }

    private FrameworkElement BuildModuleListHost()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Tools & Features",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            Margin = new Thickness(0, 0, 0, 12)
        });
        scroll.Content = panel;
        _modulePanel = panel;
        return scroll;
    }

    private void BuildModuleList()
    {
        var modules = new (string Icon, string Name, string Desc, MouseButtonEventHandler Click)[]
        {
            ("\U0001F527", "Maintenance Preview", "See exactly what a cleanup would delete, before deleting it.", MaintenancePreview_Click),
            ("\U0001F5DE", "System Check", "See what is using RAM and disk, and what to do about it.", AiAdvisor_Click),
            ("\U0001F4CB", "Diagnostics", "CPU load, uptime, disk health and your top memory users.", Diagnostics_Click),
            ("\U0001F6AB", "Privacy Shield", "Block advertising ID, user telemetry, and background tracking.", PrivacyShield_Click),
            ("\U0001F4A0", "Removable Apps", "A curated list of bundled apps that are safe to remove.", Bloatware_Click),
            ("⚡", "RAM & Network", "Flush memory caches, trim working set, and switch DNS servers.", Booster_Click),
            ("\U0001F5D1", "Uninstall Programs", "Browse everything Windows can uninstall, and remove it safely.", UninstallPrograms_Click),
            ("\u21C4", "History & Undo", "Review past actions and revert the ones that can be reverted.", UndoCenter_Click),
        };

        if (_modulePanel == null) return;
        _modulePanel.Children.Clear();
        foreach (var (icon, name, desc, click) in modules)
            _modulePanel.Children.Add(MakeModuleCard(icon, name, desc, click));
    }

    private void BuildDnsPanel()
    {
        var panel = new Border
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "DNS Server",
            Style = (Style)FindResource("SectionHeader"),
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(new TextBlock
        {
            Text = "Choose a DNS resolver",
            FontSize = 11,
            Foreground = TextTertiary,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var active = new Border
        {
            Background = PrimaryLight,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var activeRow = new StackPanel { Orientation = Orientation.Horizontal };
        activeRow.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Success,
            Margin = new Thickness(0, 0, 8, 0)
        });
        _dnsActiveLabel = new TextBlock
        {
            Text = "Active: " + VM.ActiveDns,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Primary,
            FontFamily = MonoFont
        };
        activeRow.Children.Add(_dnsActiveLabel);
        active.Child = activeRow;
        body.Children.Add(active);

        var list = new ItemsControl { ItemsSource = VM.DnsResolvers, Margin = new Thickness(0, 0, 0, 4) };
        list.ItemTemplate = BuildDnsRowTemplate();
        body.Children.Add(list);

        panel.Child = body;
        Sidebar.Children.Add(panel);
        Grid.SetRow(panel, 0);
    }

    /// <summary>
    /// Built in code rather than XAML because it binds to a converter defined
    /// in code-behind. Could move to a DataTemplate resource if that converter
    /// is ever needed elsewhere.
    /// </summary>
    private DataTemplate BuildDnsRowTemplate()
    {
        var row = new FrameworkElementFactory(typeof(Border));
        row.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        row.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        row.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        row.SetValue(Border.BorderBrushProperty, Hairline);
        // HorizontalAlignment is what actually gives the text room. Without it the
        // Border is measured to the dot's width and the labels get no space.
        row.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        row.SetValue(Border.PaddingProperty, new Thickness(6, 4, 6, 4));
        row.AddHandler(UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler((s, _) =>
            {
                if (s is FrameworkElement { DataContext: Models.DnsResolver dns } fe)
                    (Window.GetWindow(fe) as MainWindow)?.VM.SelectDnsCommand.Execute(dns);
            }));

        var textPanel = new FrameworkElementFactory(typeof(StackPanel));
        textPanel.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetValue(TextBlock.FontSizeProperty, 13.0);
        name.SetValue(TextBlock.ForegroundProperty, TextPrimary);
        name.SetBinding(TextBlock.TextProperty, new Binding("Name"));

        var ping = new FrameworkElementFactory(typeof(TextBlock));
        ping.SetValue(TextBlock.FontSizeProperty, 11.0);
        ping.SetValue(TextBlock.ForegroundProperty, TextTertiary);
        ping.SetBinding(TextBlock.TextProperty, new Binding("Ping"));

        textPanel.AppendChild(name);
        textPanel.AppendChild(ping);

        // Both the row and this panel must stretch horizontally, or the StackPanel
        // collapses to the dot's width and the name/ping text is never shown.
        var content = new FrameworkElementFactory(typeof(StackPanel));
        content.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        content.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.AppendChild(BuildDnsDot());
        content.AppendChild(textPanel);

        row.AppendChild(content);

        var template = new DataTemplate();
        template.VisualTree = row;
        return template;
    }

    private FrameworkElementFactory BuildDnsDot()
    {
        var dot = new FrameworkElementFactory(typeof(Ellipse));
        dot.SetValue(Ellipse.WidthProperty, 8.0);
        dot.SetValue(Ellipse.HeightProperty, 8.0);
        dot.SetValue(Ellipse.MarginProperty, new Thickness(0, 0, 8, 0));
        dot.SetValue(Ellipse.VerticalAlignmentProperty, VerticalAlignment.Center);
        dot.SetBinding(Ellipse.FillProperty, new Binding("Active") { Converter = new BooleanToBrushConverter() });
        return dot;
    }

    private void BuildLogPanel()
    {
        var panel = new Border { Style = (Style)FindResource("Card") };
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "Activity Log",
            Style = (Style)FindResource("SectionHeader"),
            Margin = new Thickness(0, 0, 0, 10)
        });

        var clear = new Button { Content = "↓ Clear", Padding = new Thickness(8, 4, 8, 4), FontSize = 11 };
        clear.Click += (_, _) => VM.ClearLogsCommand.Execute(null);
        Grid.SetColumn(clear, 1);
        header.Children.Add(clear);

        _logList = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontFamily = MonoFont,
            Foreground = TextSecondary,
            FontSize = 11,
            MaxHeight = 200,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemsSource = VM.Logs,
            ItemContainerStyle = (Style)FindResource("FlatListItem")
        };
        // Wrap long log lines instead of clipping them mid-word.
        var logTpl = new DataTemplate();
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new Binding());
        factory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        factory.SetValue(TextBlock.FontFamilyProperty, MonoFont);
        factory.SetValue(TextBlock.FontSizeProperty, 11.0);
        factory.SetValue(TextBlock.ForegroundProperty, TextSecondary);
        logTpl.VisualTree = factory;
        _logList.ItemTemplate = logTpl;
        _logList.SetBinding(ScrollViewer.HorizontalScrollBarVisibilityProperty,
            new Binding { Source = ScrollBarVisibility.Disabled });

        body.Children.Add(header);
        Grid.SetRow(header, 0);
        body.Children.Add(_logList);
        Grid.SetRow(_logList, 1);

        panel.Child = body;
        Sidebar.Children.Add(panel);
        Grid.SetRow(panel, 1);
    }

    private void WireVmEvents()
    {
        // These are assigned by BuildLogPanel/BuildDnsPanel/BuildActionRow, all
        // of which run before this method. Guarded anyway because the old
        // single BuildUI() had no such ordering guarantee.
        var statusLabel = _statusLabel;
        var optimizeButton = _optimizeButton;
        var logList = _logList;
        var dnsLabel = _dnsActiveLabel;

        VM.SystemStatsChanged += () => Dispatcher.Invoke(() =>
        {
            if (SubScreenOverlay.Visibility == Visibility.Visible) return;
            UpdateLiveCards();
            UpdateScoreDisplay(VM.Score);
            if (dnsLabel != null) dnsLabel.Text = "Active: " + VM.ActiveDns;
        });

        VM.HealthScoreChanged += score => Dispatcher.Invoke(() => UpdateScoreDisplay(score));

        VM.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.StatusMessage):
                    if (statusLabel != null)
                        Dispatcher.Invoke(() => statusLabel.Text = VM.StatusMessage);
                    break;
                case nameof(MainViewModel.IsOptimizing):
                    if (optimizeButton != null)
                        Dispatcher.Invoke(() => optimizeButton.IsEnabled = !VM.IsOptimizing);
                    break;
            }
        };

        // No trimming here. Mutating a ListBox's Items while ItemsSource is set
        // throws InvalidOperationException; the VM already caps Logs at 300.
        VM.LogAdded += _ => Dispatcher.Invoke(() =>
        {
            if (logList != null && logList.Items.Count > 0)
                logList.ScrollIntoView(logList.Items[^1]);
        });

        UpdateScoreDisplay(VM.Score);
        if (dnsLabel != null) dnsLabel.Text = "Active: " + VM.ActiveDns;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private bool HasBattery()
    {
        if (_batteryChecked) return _hasBattery ?? false;

        // The probe itself is a WMI round-trip (~100-400ms), so never run it on
        // the dispatcher. Seed a background probe during startup and report
        // "unknown" until it lands; the next refresh picks up the real answer.
        _batteryChecked = true;
        _hasBattery = null;
        _ = Task.Run(() =>
        {
            // Default to "no battery" so a failed probe still resolves the card.
            // A desktop is the overwhelmingly common case, and reporting AC when
            // we genuinely do not know is closer to the truth than leaving the
            // placeholder up forever.
            var found = false;
            try
            {
                using var mos = new System.Management.ManagementObjectSearcher("SELECT EstimatedChargeRemaining FROM Win32_Battery");
                found = mos.Get().Count > 0;
            }
            catch
            {
                // WMI can be disabled or slow to start on some machines. The
                // card must still leave "Detecting...".
            }

            Dispatcher.Invoke(() => { _hasBattery = found; UpdateLiveCards(); });
        });
        return false;
    }

    private void RelaunchElevated()
    {
        if (Services.SystemOptimizerService.IsElevated())
        {
            ShowSub("Already administrator", "DNS changes and app removal are available in this window.");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? System.IO.Path.Combine(AppContext.BaseDirectory, "WinCare Pro.exe"),
                UseShellExecute = true,
                Verb = "runas"
            });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ShowSub("Administrator cancelled", "WinCare stays open. RAM, disk, temp cleanup, and privacy still work. DNS changes and some uninstalls need administrator.");
        }
    }

    private void UpdateClock() => _clockDisplay.Text = DateTime.Now.ToString("hh:mm tt");
    private void UpdateLiveCards()
    {
        _ramVal.Text = VM.RamPercent.ToString("F0") + "%";
        _ramSub.Text = VM.TotalRamGb.ToString("F1") + " GB total / " + VM.RamPercent.ToString("F0") + "% used";

        _storageVal.Text = VM.StoragePercent.ToString("F0") + "%";
        _storageSub.Text = VM.FreeStorageGb.ToString("F0") + " GB free";

        // Battery only exists on laptops/tablets. On a desktop there is no
        // Win32_Battery instance, so report that honestly instead of faking
        // a charge percentage.
        // _hasBattery is null until the background probe finishes, so show a neutral
        // placeholder rather than claiming "desktop" on the first frame.
        switch (_hasBattery)
        {
            case true:
                _powerVal.Text = VM.BatteryPercent.ToString("F0") + "%";
                _powerSub.Text = VM.BatteryPercent.ToString("F0") + "% charge";
                break;
            case false:
                _powerVal.Text = "AC";
                _powerSub.Text = "No battery — desktop";
                break;
            default:
                _powerVal.Text = "—";
                _powerSub.Text = "Detecting…";
                break;
        }

        if (_headerBattery != null)
            _headerBattery.Text = _hasBattery == true
                ? VM.BatteryPercent.ToString("F0") + "%"
                : _hasBattery == false ? "AC" : "";
    }

    private void UpdateScoreDisplay(double pct)
    {
        if (_scoreLabelBig == null || _scoreRing == null) return;
        var clamped = Math.Clamp(pct, 0, 100);
        _scoreLabelBig.Text = ((int)clamped).ToString();

        var colour = clamped >= 80 ? Success : clamped >= 50 ? Warning : ErrorBrush;
        _scoreRing.Stroke = colour;
        if (_scoreRing.Effect is DropShadowEffect glow)
            glow.Color = ((SolidColorBrush)colour).Color;

        // Drive the visible arc from the score. Without this the ring was a
        // solid circle and conveyed nothing. DashArray is set directly
        // because DoubleCollection is not animatable by DoubleAnimation.
        var filled = _ringCircumference * (clamped / 100.0);
        _scoreRing.StrokeDashArray = new DoubleCollection([filled, _ringCircumference]);
    }

    private Border MakeModuleCard(string icon, string name, string desc, MouseButtonEventHandler click)
    {
        var c = new Border { Style = (Style)FindResource("ModuleCard"), Focusable = true };
        if (click != null) c.MouseLeftButtonUp += click;
        // Keyboard access: Enter/Space on a focused card should open the same panel.
        c.KeyDown += (s, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { click?.Invoke(s, new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)); e.Handled = true; }
        };
        // Screen reader access: the card's visible name becomes its automation name.
        AutomationProperties.SetName(c, name);

        var gp = new Grid();
        gp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        gp.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Emoji need Segoe UI Emoji explicitly. SansFont resolves to Segoe UI, which
// has no colour glyphs for these code points, so the icons rendered blank.
        var ip = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(ip, 0);
        ip.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = EmojiFont,
            // Set explicitly rather than inherited. The window default is a
            // light-theme colour, and any ancestor that supplies white (a
            // button template, say) turns the icon invisible on a light card.
            Foreground = Primary
        });
        gp.Children.Add(ip);

        var tp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tp, 1);
        tp.Margin = new Thickness(14, 0, 0, 0);
        tp.Children.Add(new TextBlock { Text = name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary, FontFamily = SansFont });
        tp.Children.Add(new TextBlock
        {
            Text = desc,
            FontSize = 12,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            FontFamily = SansFont
        });
        gp.Children.Add(tp);
        c.Child = gp;
        return c;
    }

    private Border MakeTelCard(string icon, string title, TextBlock valRef, TextBlock subRef, MouseButtonEventHandler click)
    {
        var c = new Border { Style = (Style)FindResource("TelemetryCard"), Focusable = true };
        if (click != null) c.MouseLeftButtonUp += click;
        c.KeyDown += (s, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                click?.Invoke(s, new MouseButtonEventArgs(
                    Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left));
                e.Handled = true;
            }
        };
        AutomationProperties.SetName(c, title);

        var gp = new Grid();
        gp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        gp.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        gp.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var iconRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        iconRow.Children.Add(new TextBlock
        {
            Text = icon,
            FontSize = 26,
            FontFamily = EmojiFont,
            // Explicit for the same reason as the module-card icons: an
            // inherited brush is one style change away from invisible.
            Foreground = Primary,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        iconRow.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            VerticalAlignment = VerticalAlignment.Bottom
        });
        gp.Children.Add(iconRow);
        Grid.SetRow(iconRow, 0);

        valRef.Style = (Style)FindResource("MetricValue");
        valRef.Text = "—";
        gp.Children.Add(valRef);
        Grid.SetRow(valRef, 1);

        subRef.Style = (Style)FindResource("MetricCaption");
        gp.Children.Add(subRef);
        Grid.SetRow(subRef, 2);

        c.Child = gp;
        return c;
    }

    // ── Sub-screen overlay ────────────────────────────────────────────────
    private void ShowSub(string title, string body) => ShowSubCore(title, body == null ? null : new TextBlock
    {
        Text = body,
        FontSize = 14,
        Foreground = TextSecondary,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = SansFont,
        MaxWidth = 480
    });

    private void ShowSub(string title, FrameworkElement content) => ShowSubCore(title, content);

    private void ShowSubCore(string title, FrameworkElement? content)
    {
        // The panel needs its own opaque surface. Previously the StackPanel sat
        // directly on the translucent overlay, so the main window's text showed
        // through the panel and made the sub-screen unreadable.
        // No Margin here: the panel Border supplies the padding. The old 40px margin
        // was left over from when this StackPanel sat directly on the overlay,
        // and combined with the panel padding it left less room than the content
        // needed, so the heading and the close button were clipped away.
        var sp = new StackPanel();
        var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            FontFamily = SansFont
        });
        // Pinned close in the header. The previous close button sat at the very
        // bottom of the scroll, so on long panels (RAM, Storage, Uninstall) the
        // only way out was to scroll the entire list first.
        var headerClose = new Button
        {
            Content = "\u2715",
            Padding = new Thickness(10, 6, 10, 6),
            Background = SurfaceHover,
            Foreground = TextSecondary,
            BorderBrush = Hairline,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        AutomationProperties.SetName(headerClose, "Close panel");
        headerClose.Click += (s, t) => HideSub();
        Grid.SetColumn(headerClose, 1);
        titleRow.Children.Add(headerClose);
        sp.Children.Add(titleRow);
        if (content != null) sp.Children.Add(content);
        var closeBtn = new Button
        {
            Content = "\u2715  Close",
            Margin = new Thickness(0, 24, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 10, 16, 10),
            Background = BgBrush,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            BorderThickness = new Thickness(1, 1, 1, 1),
            BorderBrush = Hairline,
            Cursor = Cursors.Hand
        };
        closeBtn.Click += (s, t) => HideSub();
        sp.Children.Add(closeBtn);

        // Scroll rather than clip. Several panels list variable-length content,
        // and a tall list used to push the heading and the close button out of
        // the panel entirely, leaving no way out of the sub-screen.
        var panelHost = new Border
        {
            Background = Surface,
            CornerRadius = new CornerRadius(12),
            BorderBrush = Hairline,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(32),
            MaxHeight = 640,
            MaxWidth = 800,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
                        { BlurRadius = 32, ShadowDepth = 4, Opacity = 0.18, Color = Colors.Black },
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = sp
            }
        };
        SubScreenOverlay.Child = panelHost;
        SubScreenOverlay.Visibility = Visibility.Visible;
        SubScreenOverlay.Opacity = 0;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        SubScreenOverlay.BeginAnimation(OpacityProperty, anim);

        // Esc closes the panel. Handled at the window in the Preview phase
        // because the overlay is a Border and does not receive key events until
        // something inside it holds focus.
        PreviewKeyDown -= OnPreviewKeyDown;
        PreviewKeyDown += OnPreviewKeyDown;

        // Cycle focus inside the panel so Tab cannot walk out into the
        // dashboard behind the overlay.
        KeyboardNavigation.SetTabNavigation(panelHost, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetTabNavigation(sp, KeyboardNavigationMode.Cycle);

        // Land keyboard focus on the header close button rather than nowhere.
        SubScreenOverlay.Dispatcher.BeginInvoke(
            DispatcherPriority.Input, new Action(() => headerClose.Focus()));
    }

    /// <summary>
    /// Closes an open sub-screen on Esc, and swallows Escape for the window when
    /// no panel is open so it does nothing surprising.
    /// </summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (SubScreenOverlay.Visibility != Visibility.Visible) return;
        if (e.Key != Key.Escape) return;
        HideSub();
        e.Handled = true;
    }

    private void HideSub()
    {
        SubScreenOverlay.Visibility = Visibility.Collapsed;
        SubScreenOverlay.Opacity = 1;

        // Detach the Esc handler so it stops intercepting keys once no panel is
        // open. Leaving it attached meant every future Escape was swallowed.
        PreviewKeyDown -= OnPreviewKeyDown;

        // Return focus to the dashboard rather than dropping it, otherwise
        // keyboard focus falls back to the window and Tab order starts from the
        // top of the visual tree again.
        if (MainColumn.IsEnabled) MainColumn.Focus();
    }

    private Button MakeActionButton(string text, Brush accent, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = text,
            Background = Surface,
            Foreground = accent,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            FontFamily = MonoFont,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        b.Click += onClick;
        return b;
    }

    private async void OnOptimizeClick() => await OnOptimizeClick(null);

private async Task OnOptimizeClick(IReadOnlyList<CleanupCandidate>? candidates)
    {
        if (VM.IsOptimizing) return;
        var before = VM.ReclaimedGb;
        await VM.RunOptimizationCommand.ExecuteAsync(candidates);
        var gained = Math.Max(0, VM.ReclaimedGb - before);
        ShowSub("Maintenance finished",
            "Temp files cleared: " + gained.ToString("F2") + " GB\n" +
            "Memory trimmed.\n" +
            "DNS cache flushed.\n\n" +
            "Score is now " + ((int)VM.Score) + " from this PC's RAM and disk.\n\n" +
            "Deleted files cannot be restored, so this is recorded as history only.");
    }

    private async void MaintenancePreview_Click(object s, MouseButtonEventArgs e)
    {
        VM.StatusMessage = "Scanning temp folders…";
        ShowSub("Maintenance Preview", "Scanning… this can take a moment.");
        try
        {
            // Scan off the UI thread, then hand back a detached list. The old
            // code read a shared VM collection the 5s poll could mutate
            // mid-iteration.
            //
            // VM is captured here, on the UI thread, because it is reached
            // through DataContext. Touching it inside the Task.Run lambda reads
            // a DependencyObject off the UI thread and throws
            // "a different thread owns it" the moment the card is clicked.
            var vm = VM;
            var candidates = await Task.Run(() => vm.ScanCleanupAndReturn());
            ShowSub("Maintenance Preview", BuildMaintenancePreviewContent(candidates));
        }
        catch (Exception ex)
        {
            ShowSub("Maintenance Preview", "Scan failed: " + ex.Message);
        }
        finally
        {
            VM.StatusMessage = "Scan complete.";
        }
    }

    private FrameworkElement BuildMaintenancePreviewContent(IReadOnlyList<CleanupCandidate> candidates)
    {
        var sp = new StackPanel { Width = 520 };
        var totalGb = candidates.Sum(c => c.Bytes) / 1_073_741_824.0;

        if (candidates.Count == 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "Nothing to clean. Your temp folders are already empty.",
                FontSize = 13,
                Foreground = TextSecondary,
                FontFamily = SansFont,
                TextWrapping = TextWrapping.Wrap
            });
            return sp;
        }

        sp.Children.Add(new TextBlock
        {
            Text = $"{candidates.Count} item(s) · {totalGb:F2} GB reclaimable",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            FontFamily = SansFont,
            Margin = new Thickness(0, 0, 0, 6)
        });
        sp.Children.Add(new TextBlock
        {
            Text = "Anything locked by a running program is skipped automatically. " +
                   "Deleting temp files cannot be undone.",
            FontSize = 12,
            Foreground = TextTertiary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var list = new StackPanel();
        foreach (var c in candidates.Take(40))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = System.IO.Path.GetFileName(c.Path);
            if (string.IsNullOrWhiteSpace(name)) name = c.Path;
            if (name.Length > 46) name = name[..46] + "…";

            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = TextPrimary,
                FontFamily = MonoFont,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            text.Children.Add(new TextBlock
            {
                Text = c.Category + (c.IsDirectory ? " · folder" : " · file"),
                FontSize = 10,
                Foreground = TextTertiary,
                FontFamily = SansFont
            });
            row.Children.Add(text);

            var size = new TextBlock
            {
                Text = FormatBytes(c.Bytes),
                FontSize = 12,
                Foreground = Primary,
                FontFamily = MonoFont,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(size, 1);
            row.Children.Add(size);
            list.Children.Add(row);
        }
        if (candidates.Count > 40)
            list.Children.Add(new TextBlock
            {
                Text = $"…and {candidates.Count - 40} more.",
                FontSize = 11,
                Foreground = TextTertiary,
                FontFamily = SansFont,
                Margin = new Thickness(0, 6, 0, 0)
            });

        sp.Children.Add(new ScrollViewer
        {
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = list
        });

        sp.Children.Add(MakeActionButton("\u26A1  Delete these files", Primary, async (sd, ev) =>
        {
            HideSub();
            // Delete exactly what was previewed, not a fresh scan.
            await OnOptimizeClick(candidates.ToList());
        }));

        return sp;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return (bytes / 1_073_741_824.0).ToString("F2") + " GB";
        if (bytes >= 1_048_576) return (bytes / 1_048_576.0).ToString("F1") + " MB";
        if (bytes >= 1024) return (bytes / 1024.0).ToString("F0") + " KB";
        return bytes + " B";
    }

    private void Diagnostics_Click(object s, MouseButtonEventArgs e)
    {
        VM.StatusMessage = "Collecting diagnostics…";
        ShowSub("Diagnostics", "Reading CPU, uptime, disk health…");

        // Capture VM on the UI thread; it is reached via DataContext and cannot
        // be dereferenced from the background lambda.
        var vm = VM;
        _ = Task.Run(() =>
        {
            try
            {
                vm.RefreshDiagnostics();
                Dispatcher.Invoke(() => ShowSub("Diagnostics", BuildDiagnosticsContent()));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => ShowSub("Diagnostics", "Diagnostics failed: " + ex.Message));
            }
            finally
            {
                Dispatcher.Invoke(() => VM.StatusMessage = "Diagnostics complete.");
            }
        });
    }

    private FrameworkElement BuildDiagnosticsContent()
    {
        var sp = new StackPanel { Width = 460 };

        void Row(string label, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = TextSecondary,
                FontFamily = SansFont
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
            g.Children.Add(v);
            sp.Children.Add(g);
        }

        Row("CPU load", VM.CpuPercent < 0 ? "Not reported" : $"{VM.CpuPercent:F0}%");
        Row("RAM", $"{VM.RamPercent:F0}% of {VM.TotalRamGb:F1} GB used");
        Row("Disk", $"{VM.StoragePercent:F0}% used · {VM.FreeStorageGb:F0} GB free");
        Row("Uptime", VM.UptimeText);
        Row("Disk health", VM.DiskHealthText);
        Row("Privacy shields", $"{VM.TrackersBlocked} of {VM.Shields.Count} blocked");
        Row("Reclaimed", $"{VM.ReclaimedGb:F2} GB this session");

        sp.Children.Add(new TextBlock
        {
            Text = "Top memory users",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            FontFamily = SansFont,
            Margin = new Thickness(0, 14, 0, 8)
        });

        if (VM.TopProcesses.Count == 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "No process data available.",
                FontSize = 12,
                Foreground = TextTertiary,
                FontFamily = SansFont
            });
        }
        else
        {
            foreach (var p in VM.TopProcesses)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(new TextBlock
                {
                    Text = $"{p.Name}  (pid {p.Pid})",
                    FontSize = 12,
                    Foreground = TextPrimary,
                    FontFamily = MonoFont
                });
                var mb = new TextBlock
                {
                    Text = FormatBytes((long)(p.WorkingSetMb * 1_073_741_824.0)),
                    FontSize = 12,
                    Foreground = TextSecondary,
                    FontFamily = MonoFont
                };
                Grid.SetColumn(mb, 1);
                g.Children.Add(mb);
                sp.Children.Add(g);
            }
        }

        sp.Children.Add(new Button
        {
            Content = "Check for updates",
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 4, 10, 4)
        });

        // Wired after construction so the button reference stays in scope for
        // the click handler without a field on the window.
        ((Button)sp.Children[sp.Children.Count - 1]).Click += async (_, _) =>
        {
            var manifestPath = System.IO.Path.Combine(
                AppContext.BaseDirectory, "update-source.txt");

            var expected = "{ \"version\": \"1.0.0\", \"url\": \"https://example.com/version.json\" }";

            if (!System.IO.File.Exists(manifestPath))
            {
                MessageBox.Show(
                    "No update source is configured.\n\n" +
                    "Create 'update-source.txt' next to the exe.\n\n" +
                    "Expected: " + expected,
                    "WinCare Pro", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // The file holds a JSON object, not a bare URL, so the endpoint has to
            // be lifted out of it before anything is fetched. ReadAllText-ing the
            // whole file and handing it to HttpClient as the URL yields
            // UriFormatException on every click, which surfaces as a confusing
            // "Update check failed" rather than a missing-endpoint message.
            string manifestUrl;
            try
            {
                var text = System.IO.File.ReadAllText(manifestPath).Trim();
                using var local = System.Text.Json.JsonDocument.Parse(text);
                var candidate = local.RootElement.TryGetProperty("url", out var u)
                    ? u.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(candidate))
                {
                    MessageBox.Show(
                        "'update-source.txt' has no 'url'.\n\n" +
                        "Expected: " + expected,
                        "WinCare Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                manifestUrl = candidate!;
            }
            catch (System.Text.Json.JsonException)
            {
                MessageBox.Show(
                    "'update-source.txt' is not valid JSON.\n\n" +
                    "Expected: " + expected,
                    "WinCare Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var checker = new Services.UpdateChecker();
            var current = System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString(3) ?? "0.0.0";

            var r = await checker.CheckAsync(manifestUrl, current);
            MessageBox.Show(
                r.Error is null
                    ? (r.IsUpdateAvailable
                        ? $"Update available: {r.LatestVersion} (you have {r.CurrentVersion})."
                        : $"You are on the latest version ({r.CurrentVersion}).")
                    : $"Update check failed: {r.Error}",
                "WinCare Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        return sp;
    }

    private void AiAdvisor_Click(object s, MouseButtonEventArgs e)
    {
        var findings = VM.GetHealthFindings();
        var body = string.Join(Environment.NewLine, findings.Select(f => "- " + f));
        ShowSub("System Check", body + Environment.NewLine + Environment.NewLine + "These readings come from this PC. Run System Maintenance to clear temp files and trim memory.");
    }

    private void PrivacyShield_Click(object s, MouseButtonEventArgs e) => ShowPrivacyShield();
    private void ShowPrivacyShield() => ShowSub("Privacy Shield", BuildPrivacyShieldContent());

    private FrameworkElement BuildPrivacyShieldContent()
    {
        var sp = new StackPanel { Width = 420 };
        sp.Children.Add(new TextBlock
        {
            Text = "Tap a shield to toggle it on or off.",
            FontSize = 13,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        foreach (var shield in VM.Shields)
        {
            var row = new Border
            {
                Background = Surface,
                BorderBrush = Hairline,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8),
                Cursor = Cursors.Hand
            };
            var rg = new Grid();
            rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rg.Children.Add(new TextBlock
            {
                Text = shield.Name,
                FontSize = 13,
                Foreground = TextPrimary,
                FontFamily = SansFont,
                VerticalAlignment = VerticalAlignment.Center
            });
            var status = new TextBlock
            {
                Text = shield.Enabled ? "● BLOCKED" : "○ ALLOWED",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                FontFamily = MonoFont,
                Foreground = shield.Enabled ? Success : TextTertiary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(status, 1);
            rg.Children.Add(status);
            row.Child = rg;
            row.MouseLeftButtonUp += (sd, ev) =>
            {
                VM.ToggleShieldCommand.Execute(shield);
                ShowPrivacyShield();
            };
            sp.Children.Add(row);
        }
        return sp;
    }

    private void Bloatware_Click(object s, MouseButtonEventArgs e)
    {
        VM.LoadBloatwareCommand.Execute(null);
        ShowBloatware();
    }
    private void ShowBloatware() => ShowSub("Removable Apps", BuildBloatwareContent());

    private FrameworkElement BuildBloatwareContent()
    {
        // Wide enough that the reason text gets a real column to wrap in.
        // At 460 the Uninstall button squeezed the text column narrow enough
        // that descriptions were cut off rather than wrapped.
        var sp = new StackPanel { Width = 620 };
        if (VM.BloatwareApps.Count == 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "Nothing on the removable list is installed. WinCare only flags apps " +
                       "from a curated list — it does not guess which of your apps you use.",
                FontSize = 13,
                Foreground = TextSecondary,
                FontFamily = SansFont
            });
            return sp;
        }
        sp.Children.Add(new TextBlock
        {
            Text = $"{VM.BloatwareApps.Count} app(s) from the removable list are installed. " +
                   "Each row explains why it is listed — remove only what you do not use.",
            FontSize = 13,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        foreach (var app in VM.BloatwareApps.ToList())
        {
            var row = new Border
            {
                Background = Surface,
                BorderBrush = Hairline,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var rg = new Grid();
            rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var textPanel = new StackPanel();
            textPanel.Children.Add(new TextBlock { Text = app.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary, FontFamily = SansFont });
            textPanel.Children.Add(new TextBlock
            {
                Text = app.Reason,
                FontSize = 11,
                Foreground = TextTertiary,
                FontFamily = SansFont,
                TextWrapping = TextWrapping.Wrap,
                // No MaxWidth here. A hard cap of 300 sat inside a star column
                // that was already narrower than 300, so long reasons were being
                // clipped rather than wrapped. Letting the column decide the
                // wrap width is what TextWrapping is for.
                Margin = new Thickness(0, 2, 0, 0)
            });
            rg.Children.Add(textPanel);
            var unBtn = MakeActionButton("Uninstall", ErrorBrush, (sd, ev) =>
            {
                VM.UninstallAppCommand.Execute(app);
                ShowBloatware();
            });
            Grid.SetColumn(unBtn, 1);
            rg.Children.Add(unBtn);
            row.Child = rg;
            sp.Children.Add(row);
        }
        return new ScrollViewer { MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
    }

    private void Booster_Click(object s, MouseButtonEventArgs e) => ShowRamNetwork();
    private void ShowRamNetwork() => ShowSub("RAM & Network", BuildRamNetworkContent());

    private FrameworkElement BuildRamNetworkContent()
    {
        var sp = new StackPanel { Width = 420 };
        sp.Children.Add(new TextBlock
        {
            Text = $"RAM usage: {VM.RamPercent:F0}% of {VM.TotalRamGb:F1} GB",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Primary,
            FontFamily = MonoFont,
            Margin = new Thickness(0, 0, 0, 6)
        });
        sp.Children.Add(new TextBlock
        {
            Text = "Flushing trims the standby memory list and working set to free up RAM immediately.",
            FontSize = 13,
            Foreground = TextSecondary,
            FontFamily = SansFont,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });
        sp.Children.Add(MakeActionButton("\u26a1 Flush Standby Memory Now", Primary, (sd, ev) =>
        {
            VM.FlushRamStandbyCommand.Execute(null);
            ShowRamNetwork();
        }));
        return sp;
    }

    private void UndoCenter_Click(object s, MouseButtonEventArgs e) => ShowUndoHistory();
    private void ShowUndoHistory() => ShowSub("History & Undo", BuildUndoHistoryContent());

    private FrameworkElement BuildUndoHistoryContent()
    {
        var sp = new StackPanel { Width = 460 };
        if (VM.UndoHistory.Count == 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = "No actions recorded yet.",
                FontSize = 13,
                Foreground = TextSecondary,
                FontFamily = SansFont
            });
            return sp;
        }
        foreach (var tx in VM.UndoHistory.ToList())
        {
            var row = new Border
            {
                Background = Surface,
                BorderBrush = Hairline,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var canRevert = tx.Reversable;
            var rg = new Grid();
            rg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var textPanel = new StackPanel();
            textPanel.Children.Add(new TextBlock { Text = tx.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextPrimary, FontFamily = SansFont });
            textPanel.Children.Add(new TextBlock { Text = tx.Description, FontSize = 11, Foreground = TextSecondary, FontFamily = SansFont, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 });
            textPanel.Children.Add(new TextBlock { Text = tx.Time.ToString("MMM d, HH:mm"), FontSize = 10, Foreground = TextTertiary, FontFamily = MonoFont, Margin = new Thickness(0, 4, 0, 0) });
            textPanel.Children.Add(new TextBlock
            {
                Text = canRevert ? "Reversible" : "Record only — deleted files cannot be restored",
                FontSize = 10,
                Foreground = canRevert ? Success : Warning,
                FontFamily = MonoFont,
                Margin = new Thickness(0, 4, 0, 0)
            });
            rg.Children.Add(textPanel);

            var revertBtn = MakeActionButton(canRevert ? "Revert" : "Dismiss", canRevert ? Warning : TextSecondary, (sd, ev) =>
            {
                VM.RevertTransactionCommand.Execute(tx);
                ShowUndoHistory();
            });
            Grid.SetColumn(revertBtn, 1);
            rg.Children.Add(revertBtn);
            row.Child = rg;
            sp.Children.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(VM.LastRevertMessage))
        {
            sp.Children.Add(new TextBlock
            {
                Text = VM.LastRevertMessage,
                FontSize = 12,
                Foreground = Warning,
                FontFamily = SansFont,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0)
            });
        }

        sp.Children.Add(new TextBlock
        {
            Text = $"History is stored at {HistoryStorePathHint}",
            FontSize = 10,
            Foreground = TextTertiary,
            FontFamily = SansFont,
            Margin = new Thickness(0, 12, 0, 0)
        });

        return new ScrollViewer { MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = sp };
    }

    private void RamCard_Click(object s, MouseButtonEventArgs e) => ShowRamToolkit();
    private void StorageCard_Click(object s, MouseButtonEventArgs e) => ShowStorageToolkit();
    private void PowerCard_Click(object s, MouseButtonEventArgs e) => ShowPowerToolkit();
}

// simple value converter for the DNS list: true = green dot, false = grey dot
public class BooleanToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush OnBrush  = new(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush OffBrush = new(Color.FromRgb(0xCB, 0xD5, 0xE1));
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is bool b && b ? OnBrush : OffBrush;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotImplementedException();
}
