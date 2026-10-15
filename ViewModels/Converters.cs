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
    private static readonly System.Windows.Media.SolidColorBrush PrimaryBrush = CreateFrozen(0x0F, 0x76, 0x6E);
    private static readonly System.Windows.Media.SolidColorBrush MutedBrush = CreateFrozen(0x5B, 0x6B, 0x80);

    private static System.Windows.Media.SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? PrimaryBrush : MutedBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BooleanToPrimaryLightBrushConverter : IValueConverter
{
    public static readonly BooleanToPrimaryLightBrushConverter Instance = new();
    private static readonly System.Windows.Media.SolidColorBrush PrimaryLightBrush = CreateFrozen(0xCC, 0xFB, 0xF0);

    private static System.Windows.Media.SolidColorBrush CreateFrozen(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is bool b && b) ? PrimaryLightBrush : System.Windows.Media.Brushes.Transparent;
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
