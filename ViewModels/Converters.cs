using System.Globalization;
using System.Windows.Data;

namespace WinCareDesktop.ViewModels;

public class InverseBooleanConverter : IValueConverter
{
    public static readonly InverseBooleanConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b) return System.Windows.Visibility.Visible;
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Windows.Visibility v) return v == System.Windows.Visibility.Visible;
        return false;
    }
}

public class BooleanToEnabledTextConverter : IValueConverter
{
    public static readonly BooleanToEnabledTextConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return b ? "✓ Enabled" : "✕ Disabled";
        return "Enabled";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public static class BooleanBoxes
{
    public static readonly object TrueBox = true;
    public static readonly object FalseBox = false;
}

public class IntGreaterThanZeroConverter : IValueConverter
{
    public static readonly IntGreaterThanZeroConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count) return count > 0;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BooleanToPrimaryBrushConverter : IValueConverter
{
    public static readonly BooleanToPrimaryBrushConverter Instance = new();
    private static readonly System.Windows.Media.SolidColorBrush FallbackPrimaryBrush = CreateFrozen(0x0F, 0x76, 0x6E);
    private static readonly System.Windows.Media.SolidColorBrush FallbackMutedBrush = CreateFrozen(0x5B, 0x6B, 0x80);

    private static System.Windows.Media.SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static System.Windows.Media.Brush ResolveResourceOrFallback(string key, System.Windows.Media.Brush fallback)
    {
        if (System.Windows.Application.Current is { } app && app.CheckAccess() &&
            app.TryFindResource(key) is System.Windows.Media.Brush resourceBrush)
        {
            return resourceBrush;
        }
        return fallback;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && b)
            ? ResolveResourceOrFallback("Primary", FallbackPrimaryBrush)
            : ResolveResourceOrFallback("TextTertiary", FallbackMutedBrush);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BooleanToPrimaryLightBrushConverter : IValueConverter
{
    public static readonly BooleanToPrimaryLightBrushConverter Instance = new();
    private static readonly System.Windows.Media.SolidColorBrush FallbackPrimaryLightBrush = CreateFrozen(0xE6, 0xF4, 0xF1);

    private static System.Windows.Media.SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not bool b || !b)
            return System.Windows.Media.Brushes.Transparent;

        if (System.Windows.Application.Current is { } app && app.CheckAccess() &&
            app.TryFindResource("PrimaryLight") is System.Windows.Media.Brush resourceBrush)
        {
            return resourceBrush;
        }

        return FallbackPrimaryLightBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b) return parameter?.ToString() ?? string.Empty;
        return Binding.DoNothing;
    }
}

public class BooleanToFlightNavBgConverter : IValueConverter
{
    public static readonly BooleanToFlightNavBgConverter Instance = new();
    private static readonly System.Windows.Media.SolidColorBrush SelectedBrush = Freeze(0x13, 0x2E, 0x35);

    private static System.Windows.Media.SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? SelectedBrush : System.Windows.Media.Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class BooleanToFlightNavBorderConverter : IValueConverter
{
    public static readonly BooleanToFlightNavBorderConverter Instance = new();
    private static readonly System.Windows.Media.SolidColorBrush SelectedBorder = Freeze(0x14, 0xB8, 0xA6);

    private static System.Windows.Media.SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? SelectedBorder : System.Windows.Media.Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class BooleanToFlightNavIconConverter : IValueConverter
{
    public static readonly BooleanToFlightNavIconConverter Instance = new();
    private static readonly System.Windows.Media.SolidColorBrush ActiveBrush = Freeze(0x2D, 0xD4, 0xBF);
    private static readonly System.Windows.Media.SolidColorBrush IdleBrush = Freeze(0x94, 0xA3, 0xB8);

    private static System.Windows.Media.SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? ActiveBrush : IdleBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class BooleanToFlightNavTitleConverter : IValueConverter
{
    public static readonly BooleanToFlightNavTitleConverter Instance = new();
    private static readonly System.Windows.Media.SolidColorBrush ActiveBrush = Freeze(0xF8, 0xFA, 0xFC);
    private static readonly System.Windows.Media.SolidColorBrush IdleBrush = Freeze(0xCB, 0xD5, 0xE1);

    private static System.Windows.Media.SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? ActiveBrush : IdleBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class SecurityLevelToBrushConverter : IValueConverter
{
    public static readonly SecurityLevelToBrushConverter Instance = new();
    private static readonly System.Windows.Media.SolidColorBrush CleanBrush = Freeze(0x04, 0x78, 0x57);
    private static readonly System.Windows.Media.SolidColorBrush ReviewBrush = Freeze(0xB4, 0x53, 0x09);
    private static readonly System.Windows.Media.SolidColorBrush WarningBrush = Freeze(0xDC, 0x26, 0x26);

    private static System.Windows.Media.SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            Services.SecurityFindingLevel.Warning => WarningBrush,
            Services.SecurityFindingLevel.Review => ReviewBrush,
            _ => CleanBrush,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
