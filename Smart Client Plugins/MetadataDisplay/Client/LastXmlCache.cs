using System;
using System.Collections.Concurrent;
using CommunitySDK;

namespace MetadataDisplay.Client
{
    internal static class LastXmlCache
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        private sealed class Entry
        {
            public string Xml;
            public DateTime UtcTimestamp;
            public int HitCount;
        }

        private static readonly ConcurrentDictionary<Guid, Entry> _byItem =
            new ConcurrentDictionary<Guid, Entry>();

        public static void Put(Guid metadataItemId, string xml)
        {
            if (metadataItemId == Guid.Empty || string.IsNullOrEmpty(xml)) return;
            var prev = _byItem.AddOrUpdate(metadataItemId,
                _ => new Entry { Xml = xml, UtcTimestamp = DateTime.UtcNow },
                (_, old) =>
                {
                    old.Xml = xml;
                    old.UtcTimestamp = DateTime.UtcNow;
                    return old;
                });
            // Log only the first few writes per item to avoid noise.
            if (prev.HitCount < 3)
            {
                prev.HitCount++;
                _log.Info($"[Cache] Put item={metadataItemId} bytes={xml.Length}");
            }
        }

        public static bool TryGet(Guid metadataItemId, out string xml, out DateTime utcCached)
        {
            xml = null; utcCached = default;
            if (metadataItemId == Guid.Empty)
            {
                _log.Info($"[Cache] TryGet rejected (empty id)");
                return false;
            }
            if (_byItem.TryGetValue(metadataItemId, out var e))
            {
                xml = e.Xml;
                utcCached = e.UtcTimestamp;
                _log.Info($"[Cache] TryGet HIT item={metadataItemId} bytes={xml.Length} age={(DateTime.UtcNow - e.UtcTimestamp).TotalSeconds:F0}s");
                return true;
            }
            _log.Info($"[Cache] TryGet MISS item={metadataItemId}");
            return false;
        }
    }
}
