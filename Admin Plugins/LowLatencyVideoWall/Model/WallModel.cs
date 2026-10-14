using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace LowLatencyVideoWall.Model
{
    /// <summary>
    /// One camera in a view, and the rectangle it occupies.
    ///
    /// Geometry is stored in the same 0 to 1000 normalised space Milestone uses for
    /// ViewAndLayoutItem.Layout, so a view is resolution independent and a 3440x1440 wall
    /// and a 1920x1080 one lay out identically.
    /// </summary>
    public sealed class PaneDefinition
    {
        public Guid CameraId { get; set; }
        public string CameraName { get; set; } = "";

        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public const int Scale = 1000;

        public XElement ToXml() => new XElement("Pane",
            new XAttribute("cameraId", CameraId),
            new XAttribute("cameraName", CameraName ?? ""),
            new XAttribute("x", X), new XAttribute("y", Y),
            new XAttribute("w", Width), new XAttribute("h", Height));

        public static PaneDefinition FromXml(XElement e) => new PaneDefinition
        {
            CameraId = ParseGuid(e.Attribute("cameraId")?.Value),
            CameraName = e.Attribute("cameraName")?.Value ?? "",
            X = ParseInt(e.Attribute("x")?.Value),
            Y = ParseInt(e.Attribute("y")?.Value),
            Width = ParseInt(e.Attribute("w")?.Value),
            Height = ParseInt(e.Attribute("h")?.Value)
        };

        internal static Guid ParseGuid(string s) => Guid.TryParse(s, out var g) ? g : Guid.Empty;

        internal static int ParseInt(string s) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    /// <summary>A named arrangement of cameras. What a rule or an assignment points at.</summary>
    public sealed class ViewProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public List<PaneDefinition> Panes { get; } = new List<PaneDefinition>();

        public override string ToString() => Name;

        public string ToXml() => new XElement("ViewProfile",
            new XAttribute("id", Id),
            new XAttribute("name", Name ?? ""),
            new XAttribute("description", Description ?? ""),
            new XElement("Panes", Panes.Select(p => p.ToXml()))).ToString();

        public static ViewProfile FromXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try
            {
                var root = XElement.Parse(xml);
                var profile = new ViewProfile
                {
                    Id = PaneDefinition.ParseGuid(root.Attribute("id")?.Value),
                    Name = root.Attribute("name")?.Value ?? "",
                    Description = root.Attribute("description")?.Value ?? ""
                };
                if (profile.Id == Guid.Empty) profile.Id = Guid.NewGuid();

                var panes = root.Element("Panes");
                if (panes != null)
                    foreach (var e in panes.Elements("Pane"))
                        profile.Panes.Add(PaneDefinition.FromXml(e));

                return profile;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Which view a Milestone user sees by default when their Smart Client starts.
    /// Keyed on the user name as the Smart Client reports it, since that is the only
    /// identity available on the client without extra rights.
    /// </summary>
    public sealed class WallAssignment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string UserName { get; set; } = "";
        public Guid ViewProfileId { get; set; }

        public string ToXml() => new XElement("Assignment",
            new XAttribute("id", Id),
            new XAttribute("user", UserName ?? ""),
            new XAttribute("viewProfileId", ViewProfileId)).ToString();

        public static WallAssignment FromXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try
            {
                var root = XElement.Parse(xml);
                var a = new WallAssignment
                {
                    Id = PaneDefinition.ParseGuid(root.Attribute("id")?.Value),
                    UserName = root.Attribute("user")?.Value ?? "",
                    ViewProfileId = PaneDefinition.ParseGuid(root.Attribute("viewProfileId")?.Value)
                };
                if (a.Id == Guid.Empty) a.Id = Guid.NewGuid();
                return a;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A camera as the Milestone configuration knows it, plus everything needed to open an
    /// RTSP session against it directly. Credentials are read live and never persisted.
    /// </summary>
    public sealed class WallCamera
    {
        public Guid CameraId { get; set; }
        public string Name { get; set; } = "";
        public string HardwarePath { get; set; } = "";
        public string Address { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Model { get; set; } = "";
        public string RecordingServer { get; set; } = "";

        /// <summary>Channel on the hardware, for multi-channel encoders. Zero based.</summary>
        public int Channel { get; set; }

        public bool IsAxis => (Model ?? "").IndexOf("axis", StringComparison.OrdinalIgnoreCase) >= 0
                              || (Address ?? "").IndexOf("axis", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Host or IP with any scheme and path stripped, for VAPIX and RTSP.</summary>
        public string Host
        {
            get
            {
                var a = (Address ?? "").Trim();
                if (a.Length == 0) return "";
                if (Uri.TryCreate(a, UriKind.Absolute, out var uri)) return uri.Host;
                return a.Split('/')[0];
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// What the camera itself reports over VAPIX, which is not always what Milestone or even
    /// the camera's own configuration claims. DeliveredFps in particular is measured from the
    /// stream, because capture mode changes can read back as applied while the sensor is
    /// still in the old mode.
    /// </summary>
    public sealed class CameraStatus
    {
        public Guid CameraId { get; set; }
        public bool Reachable { get; set; }
        public string Error { get; set; }

        public string ProductName { get; set; } = "";
        public string FirmwareVersion { get; set; } = "";

        public string Wdr { get; set; } = "";              // on, off, or n/a in 50/60 capture mode
        public string LowLatencyMode { get; set; } = "";   // on or off
        public string CaptureMode { get; set; } = "";
        public string CaptureModeName { get; set; } = "";
        public int ConfiguredFps { get; set; }
        public double DeliveredFps { get; set; }           // measured, not claimed
        public int Rotation { get; set; }
        public string H264Profile { get; set; } = "";
        public string Resolution { get; set; } = "";

        public DateTime ReadAtUtc { get; set; }

        /// <summary>Human readable reasons this camera is not configured for low latency.</summary>
        public List<string> Warnings { get; } = new List<string>();
    }
}
