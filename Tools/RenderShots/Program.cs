using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinCareDesktop;
using WinCareDesktop.ViewModels;

namespace RenderShots;

/// <summary>
/// Renders WinCare's window to PNGs offscreen.
///
/// The point is that WPF can rasterise its own visual tree with
/// RenderTargetBitmap, so this needs neither a visible window nor a screen
/// capture API. That makes visual review possible in any environment, and it
/// works headless.
///
/// Usage:  dotnet run --project Tools/RenderShots -- [outputDir]
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = args.Length > 0
            ? args[0]
            : Path.Combine(Directory.GetCurrentDirectory(), "shots");
        Directory.CreateDirectory(outDir);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // A real WPF app installs a DispatcherSynchronizationContext, so code
        // after `await` resumes on the UI thread. Without this, continuations run
        // on the threadpool and touch DependencyObjects from the wrong thread,
        // which looks like an app bug but is an artefact of this harness.
        SynchronizationContext.SetSynchronizationContext(
            new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

        var window = new MainWindow();

        // Show it so ItemsControl templates and layout actually realise, then
        // keep it offscreen. Without this the render is empty for any panel
        // whose content only appears after a layout pass.
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Left = -20000;   // park it off-screen; we rasterise the tree
        window.Top = -20000;

        Pump();
        window.UpdateLayout();
        Pump();

        var vm = (MainViewModel)window.DataContext;

        // Settle the telemetry so the shot shows real numbers rather than
        // placeholders.
        vm.RefreshStats();
        Pump();
        window.UpdateLayout();
        Pump();

        
        Shot(Client(window), outDir, "01-main");

        // Sub-screens. Each is a private builder, so drive them the way the
        // buttons do and capture the overlay.
        CaptureSub(window, outDir, "02-maintenance-preview", vm, "MaintenancePreview");
        CaptureSub(window, outDir, "03-diagnostics", vm, "Diagnostics");
        CaptureSub(window, outDir, "04-system-check", vm, "SystemCheck");
        CaptureSub(window, outDir, "05-privacy-shield", vm, "PrivacyShield");
        CaptureSub(window, outDir, "06-removable-apps", vm, "RemovableApps");
        CaptureSub(window, outDir, "07-ram-network", vm, "RamNetwork");
        CaptureSub(window, outDir, "08-history-undo", vm, "HistoryUndo");
        CaptureSub(window, outDir, "09-uninstall-programs", vm, "Uninstall Programs");

        // The three telemetry cards now open their own tool menus.
        CaptureCard(window, outDir, "10-ram-tools", "RAM Usage");
        CaptureCard(window, outDir, "11-storage-tools", "Storage");
        CaptureCard(window, outDir, "12-power-tools", "Power");

        ReportHoverCoverage(window);

        window.Close();
        app.Shutdown();

        Console.WriteLine("Wrote PNGs to " + outDir);
        return 0;
    }

    /// <summary>
    /// Invokes a module card's click handler by matching its title, then
    /// captures the resulting overlay.
    /// </summary>
    private static void CaptureSub(Window window, string outDir, string name, MainViewModel vm, string title)
    {
        var overlay = (Border)window.FindName("SubScreenOverlay");
        if (overlay == null) return;

        overlay.Visibility = Visibility.Collapsed;

        var card = FindModuleCard(window, title);
        if (card == null)
        {
            Console.WriteLine($"  (no module card matched '{name}')");
            return;
        }

        // Does the synthetic event even reach the Border we matched?
        var reached = false;
        System.Windows.Input.MouseButtonEventHandler spy = (_, _) => reached = true;
        card.MouseLeftButtonUp += spy;

        Console.WriteLine($"  {name}: matched <{card.GetType().Name}> child={(card as Border)?.Child?.GetType().Name}");

        // Raise it exactly as a real click would: capture, down, then up. A bare
        // MouseLeftButtonUp on a Border with no capture does reach the handler,
        // but doing the full sequence keeps this honest.
        card.CaptureMouse();
        var down = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount,
            System.Windows.Input.MouseButton.Left)
        { RoutedEvent = System.Windows.UIElement.MouseLeftButtonDownEvent, Source = card };
        card.RaiseEvent(down);
        Pump();

        var up = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount + 1,
            System.Windows.Input.MouseButton.Left)
        { RoutedEvent = System.Windows.UIElement.MouseLeftButtonUpEvent, Source = card };
        card.RaiseEvent(up);
        Pump();

        // Wait for the overlay to finish fading in. Visibility flips to Visible
        // immediately but Opacity animates 0 -> 1 over 150ms, so capturing on
        // visibility alone yields a transparent frame identical to the main shot.
        for (var i = 0; i < 60; i++)
        {
            Pump();
            Thread.Sleep(25);
            if (overlay.Visibility == Visibility.Visible && overlay.Opacity >= 0.999) break;
        }

        // The maintenance and diagnostics panels finish their work on a
        // Wait for the *expected* panel, not merely for stability. The async
        // panels (maintenance scan, diagnostics) call ShowSub twice, and their
        // second call can land after the next card has already been clicked -
        // which silently produced a Diagnostics screenshot labelled System
        // Check. Matching on the heading makes each capture self-correcting.
        var settled = 0;
        var deadline = 1200;                       // ~30s ceiling
        for (var i = 0; i < deadline; i++)
        {
            Pump();
            Thread.Sleep(25);

            if (overlay.Opacity < 0.999) { settled = 0; continue; }

            // The heading alone is not enough: the async panels use the same heading for
            // their "Scanning..." placeholder and for the finished content, so
            // waiting on the heading returned early and the real content landed
            // several captures later. Also require the loading copy to be gone.
            //
            // "Looking for" matters for the same reason and more sharply. The
            // uninstall panel loads Win32 apps first and Store apps in a second
            // pass, and the second pass spawns PowerShell. Capturing while that
            // line was still visible produced a screenshot showing 95 programs
            // with no Store apps at all, which read as a bug in the feature
            // rather than a race in the harness.
            var loading = PanelTexts(overlay).Any(t =>
                t.Contains("Scanning", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Reading", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Collecting", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("Looking for", StringComparison.OrdinalIgnoreCase));

            if (!loading && OverlayTitle(overlay) == ExpectedTitle(name))
            {
                if (++settled >= 8) break;
            }
            else settled = 0;
        }

        static IEnumerable<string> PanelTexts(Border overlay) =>
            overlay.Child is Border panel && VisualTreeHelper.GetChildrenCount(panel) > 0
                ? Walk(VisualTreeHelper.GetChild(panel, 0)).OfType<TextBlock>().Select(t => t.Text ?? "")
                : Enumerable.Empty<string>();

        static string? OverlayTitle(Border overlay) =>
            overlay.Child is Border panel
                ? VisualTreeHelper.GetChildrenCount(panel) > 0
                    ? Walk(VisualTreeHelper.GetChild(panel, 0)).OfType<TextBlock>()
                        .Select(t => t.Text).FirstOrDefault()
                    : null
                : null;

        static IEnumerable<DependencyObject> Walk(DependencyObject root)
        {
            if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) yield break;
            var n = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                yield return c;
                foreach (var g in Walk(c)) yield return g;
            }
        }

        window.UpdateLayout();
        Pump();

        Console.WriteLine($"  {name}: reachedBySyntheticEvent={reached} overlay={overlay.Visibility} opacity={overlay.Opacity:F3} panelOpacity={((FrameworkElement?)overlay.Child)?.Opacity:F3} title={OverlayTitle(overlay) ?? "(none)"} expected={ExpectedTitle(name)}");

        Shot(Client(window), outDir, name);
    }

    /// <summary>
    /// Locates a module card by its title text.
    ///
    /// Matching on "a Border that contains the title text" also matches the
    /// Window's own AdornerDecorator border, which wraps the whole visual tree
    /// and so contains every title on screen. That matched first, carried no
    /// click handler, and every synthetic click went nowhere. Requiring the
    /// module-card shape - a Border whose child is a 2-column Grid with a fixed
    /// 40px icon column - disambiguates it.
    /// </summary>
    private static FrameworkElement? FindModuleCard(DependencyObject root, string token)
    {
        foreach (var node in All(root))
        {
            if (node is not Border border) continue;
            if (border.Child is not Grid grid) continue;
            if (grid.ColumnDefinitions.Count != 2) continue;
            if (grid.ColumnDefinitions[0].Width.Value != 40) continue;

            var texts = All(border).OfType<TextBlock>().Select(t => t.Text);
            if (texts.Any(t => Normalise(t) == Normalise(token))) return border;
        }
        return null;
    }

    /// <summary>
    /// Reports whether every clickable card carries a working hover style.
    /// </summary>
    /// <remarks>
    /// This deliberately does NOT rasterise a hover frame, because that is not
    /// possible here and claiming otherwise would be worse than the gap. Two
    /// approaches were tried and both failed for real reasons:
    ///
    /// 1. Writing IsMouseOver directly. It is a read-only DependencyProperty the
    ///    framework sets with an internal authorisation key. SetValue throws, and
    ///    there is no backing CLR field or internal setter to reach.
    /// 2. Moving the real OS cursor with SetCursorPos. The call returns true and
    ///    the cursor does move, but this session delivers no mouse input to WPF at
    ///    all - Mouse.DirectlyOver stays null - so IsMouseOver is never set.
    ///
    /// What this does check is the part that silently breaks: a card whose style
    /// has no hover trigger, or whose trigger sets a property to the value it
    /// already had. Both compile, both pass a structural test, and both leave the
    /// user with no click affordance. Verifying that the trigger actually *fires*
    /// still needs a real interactive session, which is stated plainly in the
    /// README rather than papered over.
    /// </remarks>
    private static void ReportHoverCoverage(Window window)
    {
        var fe = (FrameworkElement)window;

        var cards = All(fe).OfType<Border>()
            .Where(b => b.Style != null && IsClickable(b))
            .ToList();

        var withHover = 0;
        var noHover = new List<string>();

        foreach (var card in cards)
        {
            var name = All(card).OfType<TextBlock>()
                .Select(t => t.Text)
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s) && s.Any(char.IsLetter));

            if (HasHoverTrigger(card)) withHover++;
            else noHover.Add(name ?? "(untitled)");
        }

        Console.WriteLine($"  hover: {withHover}/{cards.Count} clickable card(s) define a hover style");

        foreach (var n in noHover)
            Console.WriteLine($"    no hover style: {n}");

        if (noHover.Count > 0)
            Console.WriteLine("  hover: these cards give no click affordance until clicked");
    }

    /// <summary>A card counts as clickable if it reacts to a mouse button.</summary>
    private static bool IsClickable(Border b)
    {
        // ModuleCard and TelemetryCard are the two card styles that open a
        // panel, and both set Cursor=Hand, which is the honest signal.
        return b.Cursor == System.Windows.Input.Cursors.Hand;
    }

    /// <summary>
    /// True when the card's own style defines an IsMouseOver trigger that
    /// changes at least one visual property.
    /// </summary>
    private static bool HasHoverTrigger(Border card)
    {
        // Background lives on Panel, BorderBrush on Control.
        var visual = new[]
        {
            System.Windows.Controls.Panel.BackgroundProperty,
            Control.BorderBrushProperty,
            Border.BorderThicknessProperty,
            UIElement.OpacityProperty,
            FrameworkElement.RenderTransformProperty,
        };

        foreach (var trigger in card.Style!.Triggers)
        {
            if (trigger is not System.Windows.Trigger t) continue;
            if (t.Property != UIElement.IsMouseOverProperty) continue;
            if (t.Setters.Count == 0) continue;

            // A trigger whose setters all target properties nobody can see is
            // decoration, not feedback.
            if (t.Setters.OfType<Setter>().Any(s => visual.Contains(s.Property))) return true;
        }

        return false;
    }

    private static string Normalise(string? s) =>
        (s ?? "").Replace("&", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();

    /// <summary>Filesystem-safe slug: letters and digits only.</summary>
    private static string Slug(string? s)
    {
        var cleaned = new string((s ?? "card")
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return cleaned.Length == 0 ? "card" : cleaned;
    }


    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        // ItemsControl containers hold their realised content in the *visual*
        // tree under a ContentPresenter. A logical-only walk reports zero
        // TextBlocks for a list that is plainly drawn.
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D)
            yield break;

        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var g in All(child)) yield return g;
        }
    }

    /// <summary>
    /// Clicks a telemetry card by its title. These cards carry no x:Name, so they
    /// are matched on the TelemetryCard style plus the title text.
    /// </summary>
    private static void CaptureCard(Window window, string outDir, string name, string cardTitle)
    {
        var fe = (FrameworkElement)window;
        var overlay = FindName(fe, "SubScreenOverlay") as Border;
        if (overlay == null) { Console.WriteLine($"  ({name}: no overlay)"); return; }
        overlay.Visibility = Visibility.Collapsed;

        var card = All(fe).OfType<Border>().FirstOrDefault(b =>
            b.Style != null &&
            All(b).OfType<TextBlock>().Any(t => t.Text == cardTitle));

        if (card == null) { Console.WriteLine($"  (no telemetry card matched '{cardTitle}')"); return; }

        card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount,
            System.Windows.Input.MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonUpEvent, Source = card });

        for (var i = 0; i < 200; i++)
        {
            Pump();
            Thread.Sleep(25);
            if (overlay.Visibility == Visibility.Visible && overlay.Opacity >= 0.999) break;
        }
        for (var i = 0; i < 40; i++) { Pump(); Thread.Sleep(25); }

        var title = FirstText(overlay);
        Console.WriteLine($"  {name}: overlay={overlay.Visibility} title={title ?? "(none)"}");
        Shot(Client(window), outDir, name);
    }

    private static string? FirstText(Border overlay) =>
        overlay.Child is Border panel && VisualTreeHelper.GetChildrenCount(panel) > 0
            ? Descendants(VisualTreeHelper.GetChild(panel, 0)).OfType<TextBlock>()
                .Select(t => t.Text).FirstOrDefault()
            : null;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) yield break;
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            yield return c;
            foreach (var g in Descendants(c)) yield return g;
        }
    }

    private static FrameworkElement? FindName(FrameworkElement root, string name) =>
        root.FindName(name) as FrameworkElement;
    /// <summary>
    /// The heading each card's panel shows, so a capture can confirm it is
    /// photographing the panel it asked for.
    /// </summary>
    private static string ExpectedTitle(string shot) => shot switch
    {
        "02-maintenance-preview" => "Maintenance Preview",
        "03-diagnostics" => "Diagnostics",
        "04-system-check" => "System Check",
        "05-privacy-shield" => "Privacy Shield",
        "06-removable-apps" => "Removable Apps",
        "07-ram-network" => "RAM & Network",
        "08-history-undo" => "History & Undo",
        _ => shot
    };
    /// <summary>
    /// The window's content root, i.e. the client area. Rendering the Window
    /// itself includes the non-client frame, which leaves a dead band at the
    /// bottom of every screenshot that is not a real layout bug.
    /// </summary>
    private static FrameworkElement Client(Window w) => (FrameworkElement)w.Content;

    private static void Shot(Visual root, string outDir, string name)
    {
        // Rasterise at the client area's real laid-out size.
        var element = (FrameworkElement)root;
        var w = (int)Math.Round(element.ActualWidth);
        var h = (int)Math.Round(element.ActualHeight);
        if (w <= 0 || h <= 0) { w = 1100; h = 720; }

        element.UpdateLayout();

        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        var path = Path.Combine(outDir, name + ".png");
        using var fs = File.Create(path);
        encoder.Save(fs);
        Console.WriteLine("  " + name + ".png");
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }
}
