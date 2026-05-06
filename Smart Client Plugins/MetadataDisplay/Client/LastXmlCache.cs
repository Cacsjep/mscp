using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CommunitySDK;

namespace MetadataDisplay.Client
{
    // Rolling per-channel buffer of recent metadata packets. The configuration
    // window's Inspect / preview re-extract paths read from here so they can
    // surface a packet matching the operator's selected Topic, not just
    // whichever topic happened to land last (cameras interleave loitering /
    // speed / classification topics, so single-slot caching showed misleading
    // content). Capacity is small - this is for ~recent operator interactions,
    // not history.
    internal static class LastXmlCache
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        // Max packets retained per channel. Big enough to span a typical burst
        // of interleaved topics (Axis cameras can fire 8+ different topics in
        // a second) so an Inspect on any of them finds one quickly.
        private const int MaxPerItem = 16;

        private sealed class Entry
        {
            public readonly LinkedList<(string Xml, DateTime UtcTimestamp)> Packets =
                new LinkedList<(string, DateTime)>();
            public int HitCount;
        }

        private static readonly ConcurrentDictionary<Guid, Entry> _byItem =
            new ConcurrentDictionary<Guid, Entry>();

        public static void Put(Guid metadataItemId, string xml)
        {
            if (metadataItemId == Guid.Empty || string.IsNullOrEmpty(xml)) return;
            var entry = _byItem.GetOrAdd(metadataItemId, _ => new Entry());
            lock (entry)
            {
                entry.Packets.AddFirst((xml, DateTime.UtcNow));
                while (entry.Packets.Count > MaxPerItem)
                    entry.Packets.RemoveLast();
                if (entry.HitCount < 3)
                {
                    entry.HitCount++;
                    _log.Info($"[Cache] Put item={metadataItemId} bytes={xml.Length} buffer={entry.Packets.Count}");
                }
            }
        }

        // Returns the most recently received packet for the channel. Kept for
        // callers that don't care about topic (preview re-extract, snapshot
        // seeding, etc.).
        public static bool TryGet(Guid metadataItemId, out string xml, out DateTime utcCached)
        {
            xml = null; utcCached = default;
            if (metadataItemId == Guid.Empty)
            {
                _log.Info($"[Cache] TryGet rejected (empty id)");
                return false;
            }
            if (_byItem.TryGetValue(metadataItemId, out var entry))
            {
                lock (entry)
                {
                    if (entry.Packets.Count > 0)
                    {
                        var head = entry.Packets.First.Value;
                        xml = head.Xml;
                        utcCached = head.UtcTimestamp;
                        _log.Info($"[Cache] TryGet HIT item={metadataItemId} bytes={xml.Length} age={(DateTime.UtcNow - utcCached).TotalSeconds:F0}s buffer={entry.Packets.Count}");
                        return true;
                    }
                }
            }
            _log.Info($"[Cache] TryGet MISS item={metadataItemId}");
            return false;
        }

        // Walks recent packets newest-to-oldest and returns the first whose
        // contents satisfy the predicate. Used by Inspect packet so an
        // operator who selected a specific Topic sees a packet for THAT topic
        // even when it isn't the very latest one cached.
        public static bool TryGetMatching(Guid metadataItemId, Func<string, bool> predicate,
                                          out string xml, out DateTime utcCached)
        {
            xml = null; utcCached = default;
            if (metadataItemId == Guid.Empty || predicate == null) return false;
            if (!_byItem.TryGetValue(metadataItemId, out var entry)) return false;

            // Snapshot under the lock so we can run the user-supplied predicate
            // without holding it (predicate parses XML; don't block writers).
            (string Xml, DateTime UtcTimestamp)[] snapshot;
            lock (entry)
            {
                snapshot = new (string, DateTime)[entry.Packets.Count];
                int i = 0;
                foreach (var p in entry.Packets) snapshot[i++] = p;
            }

            foreach (var p in snapshot)
            {
                bool match;
                try { match = predicate(p.Xml); }
                catch { match = false; }
                if (match)
                {
                    xml = p.Xml;
                    utcCached = p.UtcTimestamp;
                    return true;
                }
            }
            return false;
        }
    }
}
