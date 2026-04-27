using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mscp.PkiCertInstaller.ViewModels;

// Bool -> string: "Yes - exportable PFX present on this server"
// or "No - public certificate only". Used by CertDetailsDialog.
public sealed class HasKeyConverter : IValueConverter
{
    public static readonly HasKeyConverter Instance = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Yes (PFX with key on server)" : "No (public cert only)";
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
