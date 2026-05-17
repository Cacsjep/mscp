using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Mscp.PkiCertInstaller.ViewModels;

// Bool -> StreamGeometry: green check on true, red error on false.
// Pulls the path data from the Application's resource dictionary so
// we don't duplicate it in two places.
public sealed class ResultIconConverter : IValueConverter
{
    public static readonly ResultIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is true ? "IconCheck" : "IconError";
        if (Avalonia.Application.Current is { } app
            && app.TryGetResource(key, null, out var res)
            && res is StreamGeometry sg)
        {
            return sg;
        }
        return BindingOperations.DoNothing;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class ResultBrushConverter : IValueConverter
{
    public static readonly ResultBrushConverter Instance = new();
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x4C, 0xC2, 0x6E));
    private static readonly SolidColorBrush Red   = new(Color.FromRgb(0xE0, 0x63, 0x63));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? (object)Green : Red;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
