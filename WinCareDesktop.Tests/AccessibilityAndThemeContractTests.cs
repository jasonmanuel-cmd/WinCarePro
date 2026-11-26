using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WinCareDesktop.ViewModels;
using WinCareDesktop.Views;
using Xunit;

namespace WinCareDesktop.Tests;

[Collection("WPF-UI")]
[Trait("Category", "Ui")]
public class AccessibilityAndThemeContractTests
{
    private static double Contrast(Color a, Color b)
    {
        static double Lum(Color c)
        {
            static double Ch(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
        }
        var l1 = Lum(a);
        var l2 = Lum(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    [Theory]
    [InlineData("#0F172A", "#FFFFFF", 15.0)] // TextPrimary on Surface
    [InlineData("#0F172A", "#F8FAFC", 14.0)] // TextPrimary on SurfaceHover
    [InlineData("#0F172A", "#F1F5F9", 13.5)] // TextPrimary on BgBrush
    [InlineData("#0F172A", "#E6F4F1", 12.5)] // TextPrimary on PrimaryLight
    [InlineData("#475569", "#FFFFFF", 7.0)]  // TextSecondary on Surface
    [InlineData("#475569", "#F8FAFC", 6.5)]  // TextSecondary on SurfaceHover
    [InlineData("#475569", "#F1F5F9", 6.2)]  // TextSecondary on BgBrush
    [InlineData("#5B6B80", "#FFFFFF", 5.0)]  // TextTertiary on Surface
    [InlineData("#5B6B80", "#F8FAFC", 4.8)]  // TextTertiary on SurfaceHover
    [InlineData("#5B6B80", "#F1F5F9", 4.6)]  // TextTertiary on BgBrush
    [InlineData("#0F766E", "#FFFFFF", 5.0)]  // Primary on Surface
    [InlineData("#0F766E", "#F8FAFC", 4.8)]  // Primary on SurfaceHover
    [InlineData("#0F766E", "#E6F4F1", 4.5)]  // Primary on PrimaryLight
    [InlineData("#115E59", "#FFFFFF", 7.0)]  // PrimaryDark on Surface
    [InlineData("#134E4A", "#FFFFFF", 8.5)]  // PrimaryDeep on Surface
    [InlineData("#FFFFFF", "#0F766E", 5.0)]  // White on PrimaryButton
    [InlineData("#FFFFFF", "#115E59", 7.0)]  // White on PrimaryButton hover
    [InlineData("#FFFFFF", "#134E4A", 8.5)]  // White on PrimaryDeep
    [InlineData("#047857", "#FFFFFF", 5.0)]  // Success on Surface
    [InlineData("#B45309", "#FFFFFF", 4.8)]  // Warning on Surface
    [InlineData("#DC2626", "#FFFFFF", 4.5)]  // Error on Surface
    [InlineData("#5EEAD4", "#0F172A", 10.5)] // Option 3 Mint Log Text on Dark Slate Console
    public void Option3_palette_combinations_exceed_wcag_aa_contrast(string fgHex, string bgHex, double minRatio)
    {
        var fg = (Color)ColorConverter.ConvertFromString(fgHex);
        var bg = (Color)ColorConverter.ConvertFromString(bgHex);
        var actual = Contrast(fg, bg);
        Assert.True(actual >= minRatio, $"{fgHex} on {bgHex} contrast is {actual:F2}:1 (expected >= {minRatio:F1}:1)");
    }

    [Theory]
    [InlineData("PrimaryBrightColor", "#0D9488")]
    [InlineData("PrimaryColor", "#0F766E")]
    [InlineData("PrimaryDarkColor", "#115E59")]
    [InlineData("PrimaryDeepColor", "#134E4A")]
    [InlineData("PrimaryLightColor", "#E6F4F1")]
    public void Theme_dictionary_defines_exact_option3_colors(string key, string expectedHex)
    {
        UiTestHost.Run(() =>
        {
            var dict = new ResourceDictionary { Source = new Uri("pack://application:,,,/WinCare Pro;component/Themes/Theme.xaml") };
            var actual = Assert.IsType<Color>(dict[key]);
            var expected = (Color)ColorConverter.ConvertFromString(expectedHex);
            Assert.Equal(expected, actual);
        });
    }

    [Theory]
    [InlineData("BgBrush", "#F1F5F9")]
    [InlineData("Surface", "#FFFFFF")]
    [InlineData("SurfaceHover", "#F8FAFC")]
    [InlineData("PrimaryLight", "#E6F4F1")]
    [InlineData("Primary", "#0F766E")]
    [InlineData("PrimaryDark", "#115E59")]
    [InlineData("PrimaryBright", "#0D9488")]
    [InlineData("PrimaryDeep", "#134E4A")]
    public void Theme_dictionary_resolves_option3_solid_color_brushes(string brushKey, string expectedHex)
    {
        UiTestHost.Run(() =>
        {
            var window = new MainWindow();
            try
            {
                var brush = Assert.IsType<SolidColorBrush>(window.TryFindResource(brushKey));
                var expected = (Color)ColorConverter.ConvertFromString(expectedHex);
                Assert.Equal(expected, brush.Color);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Sidebar_navigation_buttons_expose_automation_names_help_text_and_focus_visuals()
    {
        UiTestHost.Run(() =>
        {
            var window = new MainWindow { Width = 1180, Height = 760 };
            window.Show();
            UiTestHost.Drain();
            try
            {
                var sidebar = (DependencyObject)window.FindName("Sidebar")!;
                var buttons = WalkVisual(sidebar).OfType<Button>().ToList();
                Assert.Equal(11, buttons.Count);

                foreach (var btn in buttons)
                {
                    var name = AutomationProperties.GetName(btn);
                    var help = AutomationProperties.GetHelpText(btn);
                    Assert.False(string.IsNullOrWhiteSpace(name), "Sidebar nav button missing AutomationProperties.Name");
                    Assert.False(string.IsNullOrWhiteSpace(help), $"Sidebar nav button '{name}' missing HelpText");
                    Assert.True(btn.Focusable);
                    Assert.NotNull(btn.FocusVisualStyle);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Options_view_exposes_accessible_license_and_preference_controls()
    {
        UiTestHost.Run(() =>
        {
            var vm = new OptionsViewModel();
            var view = new OptionsView { DataContext = vm };
            var window = new Window { Content = view, Width = 980, Height = 650 };
            window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/WinCare Pro;component/Themes/Theme.xaml") });
            window.Show();
            window.UpdateLayout();
            try
            {
                var licenseBox = Assert.IsType<TextBox>(view.FindName("LicenseKeyTextBox"));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(licenseBox)));
                foreach (var btn in WalkVisual(view).OfType<Button>())
                {
                    Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(btn)));
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InverseBooleanConverter_inverts_both_directions(bool input, bool expected)
    {
        Assert.Equal(expected, InverseBooleanConverter.Instance.Convert(input, typeof(bool), null!, CultureInfo.InvariantCulture));
        Assert.Equal(expected, InverseBooleanConverter.Instance.ConvertBack(input, typeof(bool), null!, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(true, Visibility.Visible)]
    [InlineData(false, Visibility.Collapsed)]
    [InlineData(null, Visibility.Collapsed)]
    public void BoolToVisibilityConverter_maps_visibility_accurately(object? input, Visibility expected)
    {
        Assert.Equal(expected, BoolToVisibilityConverter.Instance.Convert(input!, typeof(Visibility), null!, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData(-5, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(42, true)]
    public void IntGreaterThanZeroConverter_handles_counts(int count, bool expected)
    {
        Assert.Equal(expected, IntGreaterThanZeroConverter.Instance.Convert(count, typeof(bool), null!, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Daily", "Daily", true)]
    [InlineData("daily", "DAILY", true)]
    [InlineData("Weekly", "Daily", false)]
    [InlineData(null, "Daily", false)]
    public void StringEqualsConverter_compares_case_insensitively(string? value, string param, bool expected)
    {
        Assert.Equal(expected, StringEqualsConverter.Instance.Convert(value!, typeof(bool), param, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BooleanToPrimaryLightBrushConverter_resolves_option3_primary_light_and_transparent()
    {
        UiTestHost.Run(() =>
        {
            var selected = Assert.IsAssignableFrom<SolidColorBrush>(
                BooleanToPrimaryLightBrushConverter.Instance.Convert(true, typeof(Brush), null!, CultureInfo.InvariantCulture));
            Assert.Equal(Color.FromRgb(0xE6, 0xF4, 0xF1), selected.Color);

            var unselected = BooleanToPrimaryLightBrushConverter.Instance.Convert(false, typeof(Brush), null!, CultureInfo.InvariantCulture);
            Assert.Same(Brushes.Transparent, unselected);
        });
    }

    private static IEnumerable<DependencyObject> WalkVisual(DependencyObject root)
    {
        yield return root;
        if (root is not Visual) yield break;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var child in WalkVisual(VisualTreeHelper.GetChild(root, i)))
                yield return child;
        }
    }
}
