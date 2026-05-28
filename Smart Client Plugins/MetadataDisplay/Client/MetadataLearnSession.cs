using System;
using System.Collections.Generic;

namespace MetadataDisplay.Client
{
    // Observations rolled up per topic from a single picked / imported packet.
    // The configuration window uses this to populate the Topic / Field /
    // Source filter dropdowns from a representative packet.
    internal sealed class TopicObservation
    {
        public string Topic;
        public Dictionary<string, HashSet<string>> SourceValues = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        public Dictionary<string, string> DataKeyExamples = new Dictionary<string, string>(StringComparer.Ordinal);
        public int Count;
    }

    // Carrier for a single packet's observations - what gets handed off from
    // PacketHistoryDialog / PacketImportDialog to the configuration window.
    internal sealed class LearnSnapshot
    {
        public int PacketsReceived;
        public IReadOnlyList<TopicObservation> Topics;

        // Build a snapshot from a single XML packet. Walks every
        // NotificationMessage and groups by Topic. Empty / unparseable input
        // yields an empty snapshot rather than throwing so callers can use
        // the same null-check / Topics.Count gate either way.
        public static LearnSnapshot FromXml(string xml)
        {
            var byTopic = new Dictionary<string, TopicObservation>(StringComparer.Ordinal);
            int packets = 0;
            if (!string.IsNullOrEmpty(xml))
            {
                packets = 1;
                foreach (var msg in MetadataExtractor.Observe(xml))
                {
                    if (string.IsNullOrEmpty(msg.Topic)) continue;
                    if (!byTopic.TryGetValue(msg.Topic, out var obs))
                    {
                        obs = new TopicObservation { Topic = msg.Topic };
                        byTopic[msg.Topic] = obs;
                    }
                    obs.Count++;

                    foreach (var src in msg.Source)
                    {
                        if (!obs.SourceValues.TryGetValue(src.Key, out var set))
                        {
                            set = new HashSet<string>(StringComparer.Ordinal);
                            obs.SourceValues[src.Key] = set;
                        }
                        set.Add(src.Value);
                    }
                    foreach (var dk in msg.Data)
                    {
                        if (!obs.DataKeyExamples.ContainsKey(dk.Key))
                            obs.DataKeyExamples[dk.Key] = dk.Value;
                    }
                }
            }

            var list = new List<TopicObservation>(byTopic.Count);
            foreach (var kv in byTopic) list.Add(kv.Value);
            return new LearnSnapshot { PacketsReceived = packets, Topics = list };
        }
    }
}
