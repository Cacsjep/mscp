using System;
using System.Windows.Media;

namespace MetadataDisplay.Client.Renderers
{
    // Single source of truth for typography + non-status colors used across all
    // render types. Lets four widgets of different kinds (lamp, number, gauge,
    // text) read as one coherent product.
    //
    // Status colors (Ok/Warn/Bad) are intentionally NOT here — those remain
    // user-configurable per widget via NumericConfig.
    internal static class WidgetTheme
    {
        // ───────── Palette ─────────
        // Primary: the actual value (big text)
        public static readonly Color ValueColor = Color.FromRgb(0xF5, 0xF7, 0xF8);
        // Secondary: units, labels under values
        public static readonly Color UnitColor = Color.FromRgb(0xCF, 0xD7, 0xDA);
        public static readonly Color LabelColor = Color.FromRgb(0xCF, 0xD7, 0xDA);
        // Tertiary: scale tick labels, chip text, "subtle" hints
        public static readonly Color SubtleColor = Color.FromRgb(0xA9, 0xB5, 0xBB);
        // Quaternary: very dim secondary text (eg. setup-mode key labels)
        public static readonly Color DimColor = Color.FromRgb(0x7A, 0x83, 0x88);
        // Gauge background track
        public static readonly Color TrackColor = Color.FromRgb(0x33, 0x3B, 0x40);
        // Chip / pill background
        public static readonly Color ChipBackground = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);

        // ───────── Type scale (Comfortable density baseline) ─────────
        public const double FontDisplay = 64;     // Number widget primary value
        public const double FontValue = 34;       // Gauge widget primary value
        public const double FontText = 28;        // Text widget value
        public const double FontTitle = 14;       // widget title above the content
        public const double FontUnit = 14;        // unit suffix beside small/medium values
        public const double FontUnitLarge = 22;   // unit suffix beside FontDisplay
        public const double FontLabel = 16;       // lamp label
        public const double FontMeta = 11;        // chip / scale labels
        public const double FontTickLabel = 11;

        // ───────── Density (per-widget) ─────────
        // Multiplier applied to non-user-configured sizes only. User-configured
        // sizes (TitleFontSize, GaugeValueFontSize, TextFontSize, LampIconSize)
        // are taken as-is and density doesn't override them.
        public static double DensityScale(string density)
        {
            if (string.IsNullOrEmpty(density)) return 1.0;
            switch (density)
            {
                case "Compact":   return 0.82;
                case "Spacious":  return 1.18;
                case "Comfortable":
                default:          return 1.0;
            }
        }
    }
}
