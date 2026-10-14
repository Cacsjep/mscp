using System;
using System.IO;
using System.Xml.Linq;

namespace LowLatencyVideoWall.Config
{
    /// <summary>How a camera picture is fitted into its pane.</summary>
    public enum PaneScaling
    {
        /// <summary>Letterbox: the picture keeps its shape and the rest of the pane stays black.</summary>
        AspectRatio,

        /// <summary>Stretch the picture to the pane edges, shape be damned.</summary>
        Fill
    }

    /// <summary>
    /// What the operator chose in the Smart Client settings dialog, persisted machine wide to
    /// %ProgramData%\Milestone\LowLatencyVideoWall\client.xml. It is machine wide rather than
    /// per user because a video wall client is a fixed installation, not a desk.
    ///
    /// The renderer reads these on every pass, so a change takes effect on the next present
    /// without rebuilding the session. Nothing here affects what is streamed, only what is
    /// drawn on top of it, with the single exception of <see cref="Scaling"/>, which only
    /// changes the destination rectangle of a blit.
    /// </summary>
    public static class WallClientSettings
    {
        private static readonly object FileLock = new object();

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Milestone", "LowLatencyVideoWall", "client.xml");

        /// <summary>The classic black diagnostics box in the corner of every pane. Off by default.</summary>
        public static bool ShowDiagnostics { get; set; }

        /// <summary>The camera name strip across the top of every pane. On by default.</summary>
        public static bool ShowTitleBar { get; set; } = true;

        /// <summary>The view selector row above the wall. On by default.</summary>
        public static bool ShowViewSelection { get; set; } = true;

        /// <summary>Aspect ratio by default: a stretched picture is worse than black bars.</summary>
        public static PaneScaling Scaling { get; set; } = PaneScaling.AspectRatio;

        /// <summary>Raised after a successful <see cref="Save"/> so open walls can react.</summary>
        public static event Action Changed;

        public static void Load()
        {
            lock (FileLock)
            {
                if (!File.Exists(ConfigPath))
                {
                    SaveLocked();
                    return;
                }

                try
                {
                    var root = XDocument.Load(ConfigPath).Root;
                    if (root == null) return;

                    ShowDiagnostics = ReadBool(root, "ShowDiagnostics", ShowDiagnostics);
                    ShowTitleBar = ReadBool(root, "ShowTitleBar", ShowTitleBar);
                    ShowViewSelection = ReadBool(root, "ShowViewSelection", ShowViewSelection);

                    var scaling = root.Element("Scaling")?.Value;
                    if (!string.IsNullOrEmpty(scaling) &&
                        Enum.TryParse(scaling, true, out PaneScaling parsed))
                        Scaling = parsed;
                }
                catch (Exception ex)
                {
                    // A corrupt settings file must never stop a wall from coming up.
                    LowLatencyVideoWallDefinition.Log.Error(
                        $"Could not read {ConfigPath}, using defaults: {ex.Message}");
                }
            }
        }

        public static void Save()
        {
            lock (FileLock) SaveLocked();
            try { Changed?.Invoke(); } catch { }
        }

        private static void SaveLocked()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                new XDocument(new XElement("LowLatencyVideoWall",
                    new XElement("ShowDiagnostics", ShowDiagnostics),
                    new XElement("ShowTitleBar", ShowTitleBar),
                    new XElement("ShowViewSelection", ShowViewSelection),
                    new XElement("Scaling", Scaling.ToString())))
                    .Save(ConfigPath);
            }
            catch (Exception ex)
            {
                LowLatencyVideoWallDefinition.Log.Error($"Could not write {ConfigPath}: {ex.Message}");
            }
        }

        private static bool ReadBool(XElement root, string name, bool fallback)
        {
            var element = root.Element(name);
            return element != null && bool.TryParse(element.Value, out var value) ? value : fallback;
        }
    }
}
