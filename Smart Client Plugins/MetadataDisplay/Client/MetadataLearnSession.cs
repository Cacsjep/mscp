using System;
using System.Collections.Generic;

namespace MetadataDisplay.Client
{
    internal sealed class TopicObservation
    {
        public string Topic;
        public Dictionary<string, HashSet<string>> SourceValues = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        public Dictionary<string, string> DataKeyExamples = new Dictionary<string, string>(StringComparer.Ordinal);
        public int Count;
    }

    internal sealed class LearnSnapshot
    {
        public int PacketsReceived;
        public IReadOnlyList<TopicObservation> Topics;
    }

    // Aggregator only: takes XML packets and accumulates topic/source/key observations.
    // The live source is owned by the configuration window so a single subscription
    // feeds both preview and learn.
    internal sealed class MetadataLearnSession
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, TopicObservation> _byTopic = new Dictionary<string, TopicObservation>(StringComparer.Ordinal);
        private int _packets;

        public bool IsActive { get; private set; }
        public int PacketsReceived => _packets;

        public event Action<LearnSnapshot> Updated;

        public void Start()
        {
            IsActive = true;
        }

        public void Stop()
        {
            IsActive = false;
        }

        public void Reset()
        {
            lock (_gate)
            {
                _byTopic.Clear();
                _packets = 0;
            }
            Updated?.Invoke(Snapshot());
        }

        public void Observe(string xml)
        {
            if (!IsActive || string.IsNullOrEmpty(xml)) return;

            _packets++;
            bool changed = false;

            foreach (var msg in MetadataExtractor.Observe(xml))
            {
                if (string.IsNullOrEmpty(msg.Topic)) continue;
                lock (_gate)
                {
                    if (!_byTopic.TryGetValue(msg.Topic, out var obs))
                    {
                        obs = new TopicObservation { Topic = msg.Topic };
                        _byTopic[msg.Topic] = obs;
                        changed = true;
                    }
                    obs.Count++;

                    foreach (var src in msg.Source)
                    {
                        if (!obs.SourceValues.TryGetValue(src.Key, out var set))
                        {
                            set = new HashSet<string>(StringComparer.Ordinal);
                            obs.SourceValues[src.Key] = set;
                            changed = true;
                        }
                        if (set.Add(src.Value)) changed = true;
                    }
                    foreach (var dk in msg.Data)
                    {
                        if (!obs.DataKeyExamples.ContainsKey(dk.Key))
                        {
                            obs.DataKeyExamples[dk.Key] = dk.Value;
                            changed = true;
                        }
                    }
                }
            }

            if (changed || (_packets % 5) == 0)
                Updated?.Invoke(Snapshot());
        }

        public LearnSnapshot Snapshot()
        {
            lock (_gate)
            {
                var copy = new List<TopicObservation>(_byTopic.Count);
                foreach (var kv in _byTopic)
                {
                    var clone = new TopicObservation { Topic = kv.Value.Topic, Count = kv.Value.Count };
                    foreach (var sv in kv.Value.SourceValues)
                        clone.SourceValues[sv.Key] = new HashSet<string>(sv.Value, StringComparer.Ordinal);
                    foreach (var dk in kv.Value.DataKeyExamples)
                        clone.DataKeyExamples[dk.Key] = dk.Value;
                    copy.Add(clone);
                }
                return new LearnSnapshot { PacketsReceived = _packets, Topics = copy };
            }
        }
    }
}
