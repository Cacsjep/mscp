using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mscp.PkiCertInstaller.ViewModels;

// Bool -> string: "Connecting..." while busy, "Connect" otherwise.
public sealed class BusyConverter : IValueConverter
{
    public static readonly BusyConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Connecting..." : "Connect";
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
