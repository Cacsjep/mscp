using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Mscp.PkiCertInstaller.ViewModels;

// Two-way converter between an enum value and `bool` keyed off a string
// ConverterParameter that names the enum member. Used by RadioButton
// IsChecked bindings so a single AuthMode property drives the group.
public sealed class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null && parameter is string p
           && string.Equals(value.ToString(), p, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string p && targetType.IsEnum)
            return Enum.Parse(targetType, p);
        return Avalonia.Data.BindingOperations.DoNothing;
    }
}
