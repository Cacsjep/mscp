using System.Collections.Generic;

namespace PKI.Crypto
{
    // ISO 3166-1 alpha-2 codes for the country dropdown. Subset of the most
    // commonly seen entries; the dropdown is editable so admins can type a
    // code that's not on this list.
    public static class Iso3166Codes
    {
        public class Entry
        {
            public string Code;
            public string Name;
            public override string ToString() => $"{Code} - {Name}";
        }

        public static readonly Entry[] All = new[]
        {
            new Entry { Code = "",   Name = "(none)" },
            new Entry { Code = "AT", Name = "Austria" },
            new Entry { Code = "AU", Name = "Australia" },
            new Entry { Code = "BE", Name = "Belgium" },
            new Entry { Code = "BG", Name = "Bulgaria" },
            new Entry { Code = "BR", Name = "Brazil" },
            new Entry { Code = "CA", Name = "Canada" },
            new Entry { Code = "CH", Name = "Switzerland" },
            new Entry { Code = "CN", Name = "China" },
            new Entry { Code = "CZ", Name = "Czech Republic" },
            new Entry { Code = "DE", Name = "Germany" },
            new Entry { Code = "DK", Name = "Denmark" },
            new Entry { Code = "EE", Name = "Estonia" },
            new Entry { Code = "ES", Name = "Spain" },
            new Entry { Code = "FI", Name = "Finland" },
            new Entry { Code = "FR", Name = "France" },
            new Entry { Code = "GB", Name = "United Kingdom" },
            new Entry { Code = "GR", Name = "Greece" },
            new Entry { Code = "HR", Name = "Croatia" },
            new Entry { Code = "HU", Name = "Hungary" },
            new Entry { Code = "IE", Name = "Ireland" },
            new Entry { Code = "IL", Name = "Israel" },
            new Entry { Code = "IN", Name = "India" },
            new Entry { Code = "IS", Name = "Iceland" },
            new Entry { Code = "IT", Name = "Italy" },
            new Entry { Code = "JP", Name = "Japan" },
            new Entry { Code = "KR", Name = "South Korea" },
            new Entry { Code = "LT", Name = "Lithuania" },
            new Entry { Code = "LU", Name = "Luxembourg" },
            new Entry { Code = "LV", Name = "Latvia" },
            new Entry { Code = "MX", Name = "Mexico" },
            new Entry { Code = "NL", Name = "Netherlands" },
            new Entry { Code = "NO", Name = "Norway" },
            new Entry { Code = "NZ", Name = "New Zealand" },
            new Entry { Code = "PL", Name = "Poland" },
            new Entry { Code = "PT", Name = "Portugal" },
            new Entry { Code = "RO", Name = "Romania" },
            new Entry { Code = "RS", Name = "Serbia" },
            new Entry { Code = "SE", Name = "Sweden" },
            new Entry { Code = "SG", Name = "Singapore" },
            new Entry { Code = "SI", Name = "Slovenia" },
            new Entry { Code = "SK", Name = "Slovakia" },
            new Entry { Code = "TR", Name = "Turkey" },
            new Entry { Code = "UA", Name = "Ukraine" },
            new Entry { Code = "US", Name = "United States" },
            new Entry { Code = "ZA", Name = "South Africa" },
        };

        public static IEnumerable<Entry> AsEnumerable() => All;
    }
}
