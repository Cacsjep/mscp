using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace MetadataDisplay.Client
{
    internal sealed class ExtractorConfig
    {
        public string Topic;
        public string TopicMatchMode; // Contains | Exact | EndsWith
        public IReadOnlyList<KeyValuePair<string, string>> SourceFilters;
        public string DataKey;

        public static ExtractorConfig FromManager(MetadataDisplayViewItemManager m)
        {
            return new ExtractorConfig
            {
                Topic = m.Topic ?? "",
                TopicMatchMode = string.IsNullOrEmpty(m.TopicMatchMode) ? "Contains" : m.TopicMatchMode,
                SourceFilters = ParseSourceFilters(m.SourceFilters),
                DataKey = m.DataKey ?? "",
            };
        }

        public static IReadOnlyList<KeyValuePair<string, string>> ParseSourceFilters(string s)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(s)) return list;
            foreach (var raw in s.Split(';'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                var eq = part.IndexOf('=');
                if (eq <= 0 || eq >= part.Length - 1) continue;
                list.Add(new KeyValuePair<string, string>(
                    part.Substring(0, eq).Trim(),
                    part.Substring(eq + 1).Trim()));
            }
            return list;
        }

        public static string SerializeSourceFilters(IEnumerable<KeyValuePair<string, string>> kvs)
        {
            return string.Join(";", kvs.Select(kv => kv.Key + "=" + kv.Value));
        }
    }

    internal sealed class ExtractedValue
    {
        public string Value;
        public DateTime TimestampUtc;
        public string Topic;
    }

    internal static class MetadataExtractor
    {
        private static readonly XNamespace NsTt = "http://www.onvif.org/ver10/schema";
        private static readonly XNamespace NsWsnt = "http://docs.oasis-open.org/wsn/b-2";

        // Topics that should never be surfaced to the user: vendor-internal payloads
        // (e.g. Axis xinternal_data carries a giant SVG that's not meant for
        // operator-facing widgets and would just clutter Learn/Inspect dropdowns).
        private static readonly string[] HiddenTopicSubstrings = new[]
        {
            "xinternal_data",
        };

        public static bool IsHiddenTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return false;
            foreach (var s in HiddenTopicSubstrings)
            {
                if (topic.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        // Returns the same XML with any NotificationMessage whose Topic is hidden
        // stripped out. If the document doesn't parse, returns the original string.
        public static string FilterHiddenTopics(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return xml;
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch { return xml; }

            var toRemove = new List<XElement>();
            foreach (var nm in doc.Descendants(NsWsnt + "NotificationMessage"))
            {
                var topic = ((string)nm.Element(NsWsnt + "Topic") ?? "").Trim();
                if (IsHiddenTopic(topic)) toRemove.Add(nm);
            }
            foreach (var nm in toRemove) nm.Remove();
            return doc.ToString(SaveOptions.None);
        }

        // Returns the latest matching extraction in the document, or null.
        public static ExtractedValue TryExtract(string xml, ExtractorConfig cfg)
        {
            if (string.IsNullOrEmpty(xml) || cfg == null) return null;
            if (string.IsNullOrEmpty(cfg.DataKey)) return null;

            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch { return null; }

            ExtractedValue latest = null;

            foreach (var nm in doc.Descendants(NsWsnt + "NotificationMessage"))
            {
                var topic = ((string)nm.Element(NsWsnt + "Topic") ?? "").Trim();
                if (!TopicMatches(topic, cfg.Topic, cfg.TopicMatchMode)) continue;

                var ttMessage = nm.Element(NsWsnt + "Message")?.Element(NsTt + "Message");
                if (ttMessage == null) continue;

                var op = (string)ttMessage.Attribute("PropertyOperation");
                if (string.Equals(op, "Deleted", StringComparison.OrdinalIgnoreCase)) continue;

                if (!SourceMatches(ttMessage, cfg.SourceFilters)) continue;

                var data = ttMessage.Element(NsTt + "Data");
                if (data == null) continue;

                string value = null;
                foreach (var si in data.Elements(NsTt + "SimpleItem"))
                {
                    if (string.Equals((string)si.Attribute("Name"), cfg.DataKey, StringComparison.OrdinalIgnoreCase))
                    {
                        value = (string)si.Attribute("Value");
                        break;
                    }
                }
                if (value == null) continue;

                DateTime ts = DateTime.UtcNow;
                var utcAttr = (string)ttMessage.Attribute("UtcTime");
                if (!string.IsNullOrEmpty(utcAttr)
                    && DateTime.TryParse(utcAttr, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                {
                    ts = parsed;
                }

                if (latest == null || ts >= latest.TimestampUtc)
                {
                    latest = new ExtractedValue { Value = value, TimestampUtc = ts, Topic = topic };
                }
            }

            return latest;
        }

        private static bool TopicMatches(string actual, string filter, string mode)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (string.IsNullOrEmpty(actual)) return false;
            switch (mode)
            {
                case "Exact":
                    return string.Equals(actual, filter, StringComparison.OrdinalIgnoreCase);
                case "EndsWith":
                    return actual.EndsWith(filter, StringComparison.OrdinalIgnoreCase);
                case "Contains":
                default:
                    return actual.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        private static bool SourceMatches(XElement ttMessage, IReadOnlyList<KeyValuePair<string, string>> filters)
        {
            if (filters == null || filters.Count == 0) return true;
            var source = ttMessage.Element(NsTt + "Source");
            if (source == null) return false;

            foreach (var kv in filters)
            {
                bool matched = false;
                foreach (var si in source.Elements(NsTt + "SimpleItem"))
                {
                    var name = (string)si.Attribute("Name");
                    if (!string.Equals(name, kv.Key, StringComparison.Ordinal)) continue;
                    var val = (string)si.Attribute("Value") ?? "";
                    if (string.Equals(val, kv.Value, StringComparison.Ordinal)) { matched = true; break; }
                }
                if (!matched) return false;
            }
            return true;
        }

        // For learn-mode: enumerate every NotificationMessage's topic + Source/Data SimpleItem names.
        public static IEnumerable<ObservedMessage> Observe(string xml)
        {
            if (string.IsNullOrEmpty(xml)) yield break;
            XDocument doc;
            try { doc = XDocument.Parse(xml); }
            catch { yield break; }

            foreach (var nm in doc.Descendants(NsWsnt + "NotificationMessage"))
            {
                var topic = ((string)nm.Element(NsWsnt + "Topic") ?? "").Trim();
                if (IsHiddenTopic(topic)) continue;
                var ttMessage = nm.Element(NsWsnt + "Message")?.Element(NsTt + "Message");
                if (ttMessage == null) continue;

                var sources = new List<KeyValuePair<string, string>>();
                var src = ttMessage.Element(NsTt + "Source");
                if (src != null)
                {
                    foreach (var si in src.Elements(NsTt + "SimpleItem"))
                    {
                        var name = (string)si.Attribute("Name") ?? "";
                        var val = (string)si.Attribute("Value") ?? "";
                        if (name.Length > 0)
                            sources.Add(new KeyValuePair<string, string>(name, val));
                    }
                }

                var dataKeys = new List<KeyValuePair<string, string>>();
                var data = ttMessage.Element(NsTt + "Data");
                if (data != null)
                {
                    foreach (var si in data.Elements(NsTt + "SimpleItem"))
                    {
                        var name = (string)si.Attribute("Name") ?? "";
                        var val = (string)si.Attribute("Value") ?? "";
                        if (name.Length > 0)
                            dataKeys.Add(new KeyValuePair<string, string>(name, val));
                    }
                }

                yield return new ObservedMessage
                {
                    Topic = topic,
                    Source = sources,
                    Data = dataKeys,
                };
            }
        }
    }

    internal sealed class ObservedMessage
    {
        public string Topic;
        public IReadOnlyList<KeyValuePair<string, string>> Source;
        public IReadOnlyList<KeyValuePair<string, string>> Data;
    }
}
