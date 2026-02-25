using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using VideoOS.Platform;

namespace RtmpStreamer.Streaming
{
    /// <summary>
    /// Manages multiple concurrent StreamSessions. Handles configuration
    /// persistence (load/save) and provides a thread-safe interface for
    /// adding, removing, starting, and stopping streams.
    /// </summary>
    internal class StreamSessionManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, StreamSession> _sessions = new ConcurrentDictionary<string, StreamSession>();
        private readonly object _configLock = new object();

        /// <summary>
        /// Add and start a new stream session.
        /// Returns the session ID.
        /// </summary>
        public string AddStream(Item cameraItem, string rtmpUrl)
        {
            var session = new StreamSession(cameraItem, rtmpUrl);
            if (!_sessions.TryAdd(session.SessionId, session))
                throw new InvalidOperationException("Failed to add session");

            session.Start();
            return session.SessionId;
        }

        /// <summary>
        /// Stop and remove a stream session by its ID.
        /// </summary>
        public void RemoveStream(string sessionId)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.Stop();
                session.Dispose();
            }
        }

        /// <summary>
        /// Stop a stream session without removing it.
        /// </summary>
        public void StopStream(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Stop();
            }
        }

        /// <summary>
        /// Start a previously stopped stream session.
        /// </summary>
        public void StartStream(string sessionId)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Start();
            }
        }

        /// <summary>
        /// Get all current sessions.
        /// </summary>
        public IEnumerable<StreamSession> GetSessions()
        {
            return _sessions.Values.ToArray();
        }

        /// <summary>
        /// Stop and remove all sessions.
        /// </summary>
        public void StopAll()
        {
            foreach (var kvp in _sessions)
            {
                try
                {
                    kvp.Value.Stop();
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _sessions.Clear();
        }

        public void Dispose()
        {
            StopAll();
        }

        #region Configuration Persistence

        /// <summary>
        /// Serialized stream configuration for persistence.
        /// </summary>
        public class StreamConfig
        {
            public Guid CameraId { get; set; }
            public string CameraName { get; set; }
            public string RtmpUrl { get; set; }
            public bool AutoStart { get; set; }
        }

        /// <summary>
        /// Save current stream configurations to an XML string for storage in Milestone config.
        /// </summary>
        public string SaveConfigXml()
        {
            lock (_configLock)
            {
                var root = new XElement("RtmpStreams");
                foreach (var session in _sessions.Values)
                {
                    root.Add(new XElement("Stream",
                        new XAttribute("CameraId", session.CameraId),
                        new XAttribute("CameraName", session.CameraName),
                        new XAttribute("RtmpUrl", session.RtmpUrl),
                        new XAttribute("AutoStart", "true")
                    ));
                }
                return root.ToString();
            }
        }

        /// <summary>
        /// Load stream configurations from an XML string.
        /// Returns a list of configs that can be used to start sessions.
        /// </summary>
        public static List<StreamConfig> LoadConfigXml(string xml)
        {
            var configs = new List<StreamConfig>();
            if (string.IsNullOrWhiteSpace(xml))
                return configs;

            try
            {
                var root = XElement.Parse(xml);
                foreach (var elem in root.Elements("Stream"))
                {
                    configs.Add(new StreamConfig
                    {
                        CameraId = Guid.Parse(elem.Attribute("CameraId")?.Value ?? Guid.Empty.ToString()),
                        CameraName = elem.Attribute("CameraName")?.Value ?? "",
                        RtmpUrl = elem.Attribute("RtmpUrl")?.Value ?? "",
                        AutoStart = bool.Parse(elem.Attribute("AutoStart")?.Value ?? "true")
                    });
                }
            }
            catch
            {
                // Invalid XML - return empty list
            }

            return configs;
        }

        /// <summary>
        /// Start sessions from a list of configurations.
        /// Resolves camera items from the configuration system.
        /// </summary>
        public void StartFromConfig(List<StreamConfig> configs)
        {
            foreach (var config in configs)
            {
                if (!config.AutoStart || config.CameraId == Guid.Empty || string.IsNullOrEmpty(config.RtmpUrl))
                    continue;

                try
                {
                    // Resolve camera item from configuration
                    var cameraItem = Configuration.Instance.GetItem(config.CameraId, Kind.Camera);
                    if (cameraItem == null)
                    {
                        PluginLog.Error($"Camera not found: {config.CameraId} ({config.CameraName})");
                        continue;
                    }

                    AddStream(cameraItem, config.RtmpUrl);
                    PluginLog.Info($"Started stream: {config.CameraName} -> {config.RtmpUrl}");
                }
                catch (Exception ex)
                {
                    PluginLog.Error($"Failed to start stream for {config.CameraName}: {ex.Message}", ex);
                }
            }
        }

        #endregion
    }
}
