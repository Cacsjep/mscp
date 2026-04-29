using CommunitySDK;
using System;
using System.Collections.Generic;
using System.Linq;
using VideoOS.Platform;
using VideoOS.Platform.Background;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace ColoredTimeline.Background
{
    public class ColoredTimelineSmartClientBackgroundPlugin : BackgroundPlugin
    {
        private readonly Dictionary<ImageViewerAddOn, List<ColoredTimelineSequenceSource>> _sources
            = new Dictionary<ImageViewerAddOn, List<ColoredTimelineSequenceSource>>();

        private readonly object _lock = new object();
        private readonly PluginLog _log = new PluginLog("ColoredTimeline - SC BG");

        private List<RuleConfig> _rules = new List<RuleConfig>();
        private object _configChangedReg;

        public override Guid Id => ColoredTimelineDefinition.SmartClientBackgroundPluginId;
        public override string Name => "Colored Timeline Smart Client";
        public override List<EnvironmentType> TargetEnvironments { get; } = new List<EnvironmentType>
        {
            EnvironmentType.SmartClient
        };

        public override void Init()
        {
            _log.Info("Init - log file: %ProgramData%\\Milestone\\XProtect Smart Client\\MIPLog.txt");
            LoadRules();
            ClientControl.Instance.NewImageViewerControlEvent += OnNewImageViewer;
            _configChangedReg = EnvironmentManager.Instance.RegisterReceiver(
                OnConfigurationChanged,
                new MessageIdFilter(MessageId.Server.ConfigurationChangedIndication));
            _log.Info("Initialized - listening for image viewers and config changes");
        }

        public override void Close()
        {
            ClientControl.Instance.NewImageViewerControlEvent -= OnNewImageViewer;
            if (_configChangedReg != null)
            {
                EnvironmentManager.Instance.UnRegisterReceiver(_configChangedReg);
                _configChangedReg = null;
            }
            lock (_lock)
            {
                foreach (var kv in _sources)
                {
                    foreach (var src in kv.Value)
                    {
                        try { kv.Key.UnregisterTimelineSequenceSource(src); } catch { }
                        try { src.Dispose(); } catch { }
                    }
                }
                _sources.Clear();
            }
            _log.Info("Closed");
        }

        private object OnConfigurationChanged(Message message, FQID destination, FQID sender)
        {
            try
            {
                if (message?.RelatedFQID?.Kind != ColoredTimelineDefinition.TimelineRuleKindId) return null;

                Configuration.Instance.RefreshConfiguration(
                    message.RelatedFQID.ServerId,
                    ColoredTimelineDefinition.TimelineRuleKindId);

                LoadRules();

                lock (_lock)
                {
                    foreach (var addon in _sources.Keys.ToList())
                        Reattach(addon);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"OnConfigurationChanged failed: {ex.Message}");
            }
            return null;
        }

        private void LoadRules()
        {
            try
            {
                var items = Configuration.Instance.GetItemConfigurations(
                    ColoredTimelineDefinition.PluginId, null, ColoredTimelineDefinition.TimelineRuleKindId);

                var all = items.Select(RuleConfig.From).Where(r => r != null).ToList();
                _rules = all.Where(r => r.Enabled).ToList();
                _log.Info($"Loaded {_rules.Count} enabled rule(s) ({all.Count - _rules.Count} disabled)");
                foreach (var r in _rules)
                {
                    _log.Info($"  rule '{r.Name}': start='{r.StartEvent}' stop='{r.StopEvent}' " +
                              $"cameras={r.Cameras.Count} color=#{r.Color.R:X2}{r.Color.G:X2}{r.Color.B:X2}");
                }
            }
            catch (Exception ex)
            {
                _log.Error($"LoadRules failed: {ex.Message}");
                _rules = new List<RuleConfig>();
            }
        }

        private void OnNewImageViewer(ImageViewerAddOn addon)
        {
            if (addon == null) return;
            if (addon.ImageViewerType != ImageViewerType.CameraViewItem) return;

            addon.PropertyChangedEvent += OnImageViewerPropertyChanged;
            addon.CloseEvent += OnImageViewerClosed;
            Reattach(addon);
        }

        private void OnImageViewerPropertyChanged(object sender, EventArgs e)
        {
            if (sender is ImageViewerAddOn addon)
                Reattach(addon);
        }

        private void OnImageViewerClosed(object sender, EventArgs e)
        {
            if (!(sender is ImageViewerAddOn addon)) return;
            addon.PropertyChangedEvent -= OnImageViewerPropertyChanged;
            addon.CloseEvent -= OnImageViewerClosed;

            lock (_lock)
            {
                if (_sources.TryGetValue(addon, out var list))
                {
                    foreach (var src in list)
                    {
                        try { addon.UnregisterTimelineSequenceSource(src); } catch { }
                        try { src.Dispose(); } catch { }
                    }
                    _sources.Remove(addon);
                }
            }
        }

        private void Reattach(ImageViewerAddOn addon)
        {
            lock (_lock)
            {
                if (_sources.TryGetValue(addon, out var existing))
                {
                    foreach (var src in existing)
                    {
                        try { addon.UnregisterTimelineSequenceSource(src); } catch { }
                        try { src.Dispose(); } catch { }
                    }
                    existing.Clear();
                }
                else
                {
                    existing = new List<ColoredTimelineSequenceSource>();
                    _sources[addon] = existing;
                }

                if (addon.CameraFQID == null) return;
                var cameraId = addon.CameraFQID.ObjectId;

                int matched = 0;
                foreach (var rule in _rules)
                {
                    if (!rule.AppliesTo(cameraId)) continue;
                    try
                    {
                        var src = new ColoredTimelineSequenceSource(addon.CameraFQID, rule);
                        addon.RegisterTimelineSequenceSource(src);
                        existing.Add(src);
                        matched++;
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"Failed to register source for rule '{rule.Name}': {ex.Message}");
                    }
                }
                _log.Info($"Pane cam={cameraId} attached: {matched}/{_rules.Count} rule(s) matched");
            }
        }

        internal class RuleConfig
        {
            public string Name { get; private set; }
            public bool Enabled { get; private set; }
            public string StartEvent { get; private set; }
            public string StopEvent { get; private set; }
            public System.Drawing.Color Color { get; private set; }
            public HashSet<Guid> Cameras { get; private set; }

            public bool AppliesTo(Guid cameraId) => Cameras.Contains(cameraId);

            public static RuleConfig From(Item item)
            {
                if (item?.Properties == null) return null;
                try
                {
                    var enabled = !item.Properties.ContainsKey("Enabled") || item.Properties["Enabled"] != "No";
                    var start = item.Properties.ContainsKey("StartEvent") ? item.Properties["StartEvent"] : "";
                    var stop = item.Properties.ContainsKey("StopEvent") ? item.Properties["StopEvent"] : "";
                    var colorHex = item.Properties.ContainsKey("RibbonColor") ? item.Properties["RibbonColor"] : "#1E88E5";
                    var camIds = item.Properties.ContainsKey("CameraIds") ? item.Properties["CameraIds"] : "";

                    var cams = new HashSet<Guid>();
                    foreach (var s in camIds.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        if (Guid.TryParse(s.Trim(), out var g)) cams.Add(g);

                    System.Drawing.Color color;
                    try { color = System.Drawing.ColorTranslator.FromHtml(colorHex); }
                    catch { color = System.Drawing.Color.DodgerBlue; }

                    return new RuleConfig
                    {
                        Name = item.Name,
                        Enabled = enabled,
                        StartEvent = start,
                        StopEvent = stop,
                        Color = color,
                        Cameras = cams
                    };
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
