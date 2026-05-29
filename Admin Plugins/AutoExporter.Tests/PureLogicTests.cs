using System;
using AutoExporter.Background;
using Xunit;

namespace AutoExporter.Tests
{
    public class SubtractRangeTests
    {
        private static readonly DateTime Anchor = new DateTime(2026, 5, 28, 14, 30, 0, DateTimeKind.Utc);

        [Fact]
        public void Minutes()
        {
            Assert.Equal(Anchor.AddMinutes(-90), TimeRange.Subtract(Anchor, 90, "Minutes"));
            // case-insensitive
            Assert.Equal(Anchor.AddMinutes(-1),  TimeRange.Subtract(Anchor, 1, "minutes"));
        }

        [Fact]
        public void Hours()
        {
            Assert.Equal(Anchor.AddHours(-2), TimeRange.Subtract(Anchor, 2, "Hours"));
        }

        [Fact]
        public void Days_is_default_for_unknown_unit()
        {
            Assert.Equal(Anchor.AddDays(-5), TimeRange.Subtract(Anchor, 5, "Days"));
            // unknown unit falls back to Days
            Assert.Equal(Anchor.AddDays(-5), TimeRange.Subtract(Anchor, 5, "wibble"));
            Assert.Equal(Anchor.AddDays(-5), TimeRange.Subtract(Anchor, 5, null));
        }

        [Fact]
        public void Months_treated_as_30_days_each()
        {
            Assert.Equal(Anchor.AddDays(-60), TimeRange.Subtract(Anchor, 2, "Months"));
        }

        [Fact]
        public void Zero_or_negative_value_clamps_to_one_unit()
        {
            // Documented behaviour: invalid range value becomes 1 (so "last 0 days" → "last 1 day")
            Assert.Equal(Anchor.AddDays(-1), TimeRange.Subtract(Anchor, 0, "Days"));
            Assert.Equal(Anchor.AddDays(-1), TimeRange.Subtract(Anchor, -5, "Days"));
        }
    }

    public class MakeSafeFileNameTests
    {
        [Fact]
        public void Replaces_invalid_chars_with_underscore()
        {
            // Forward and back slashes, colon, asterisk, question mark — all invalid on Windows
            Assert.Equal("a_b_c__d_", Exporter.MakeSafeFileName("a/b\\c:*d?"));
        }

        [Fact]
        public void Leaves_valid_chars_alone()
        {
            Assert.Equal("Camera 01 - Lobby", Exporter.MakeSafeFileName("Camera 01 - Lobby"));
            Assert.Equal("dotted.name.ext",   Exporter.MakeSafeFileName("dotted.name.ext"));
        }

        [Fact]
        public void Empty_or_null_returns_underscore_placeholder()
        {
            Assert.Equal("_", Exporter.MakeSafeFileName(""));
            Assert.Equal("_", Exporter.MakeSafeFileName(null));
        }

        [Fact]
        public void Unicode_passthrough()
        {
            Assert.Equal("Östra parkering", Exporter.MakeSafeFileName("Östra parkering"));
        }
    }
}
