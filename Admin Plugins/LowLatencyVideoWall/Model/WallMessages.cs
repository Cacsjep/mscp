using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace LowLatencyVideoWall.Model
{
    /// <summary>
    /// The message a rule action sends from the Event Server to every Smart Client, telling
    /// one user's clients to switch the wall to a view.
    ///
    /// MessageCommunication carries an object as the message data, but a plain string payload
    /// keeps the Event Server and the Smart Client from needing to agree on an assembly
    /// version, which matters because they can be upgraded independently.
    /// </summary>
    public static class WallMessages
    {
        /// <summary>Message id for a wall switch to a stored view. Sent server to client.</summary>
        public const string SetWallView = "LowLatencyVideoWall.SetWallView";

        /// <summary>
        /// Message id for a wall the rule composes itself: a set of cameras, one camera in a
        /// numbered position, or nothing at all. Sent server to client.
        /// </summary>
        public const string SetWallCameras = "LowLatencyVideoWall.SetWallCameras";

        /// <summary>What a SetWallCameras message is asking the wall to do.</summary>
        public enum WallCommand
        {
            /// <summary>Replace the whole wall with these cameras in a best fit grid.</summary>
            Replace,

            /// <summary>Put one camera in one numbered position and leave the rest alone.</summary>
            PlaceAt,

            /// <summary>Show nothing.</summary>
            Clear
        }

        public sealed class WallCameraRef
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
        }

        public sealed class SetWallCamerasPayload
        {
            public string UserName { get; set; } = "";
            public WallCommand Command { get; set; } = WallCommand.Replace;

            /// <summary>One based, and only meaningful for <see cref="WallCommand.PlaceAt"/>.</summary>
            public int Position { get; set; }

            public List<WallCameraRef> Cameras { get; } = new List<WallCameraRef>();
            public DateTime SentUtc { get; set; } = DateTime.UtcNow;

            public string ToXml()
            {
                var root = new XElement("SetWallCameras",
                    new XAttribute("user", UserName ?? ""),
                    new XAttribute("command", Command.ToString()),
                    new XAttribute("position", Position.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("sentUtc", SentUtc.ToString("O", CultureInfo.InvariantCulture)));

                foreach (var camera in Cameras.Where(c => c != null))
                {
                    root.Add(new XElement("Camera",
                        new XAttribute("id", camera.Id),
                        new XAttribute("name", camera.Name ?? "")));
                }

                return root.ToString();
            }

            public static SetWallCamerasPayload FromXml(string xml)
            {
                if (string.IsNullOrWhiteSpace(xml)) return null;
                try
                {
                    var root = XElement.Parse(xml);

                    var payload = new SetWallCamerasPayload
                    {
                        UserName = root.Attribute("user")?.Value ?? "",
                        Position = PaneDefinition.ParseInt(root.Attribute("position")?.Value),
                        SentUtc = DateTime.TryParse(root.Attribute("sentUtc")?.Value,
                            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                            ? t : DateTime.UtcNow
                    };

                    if (Enum.TryParse(root.Attribute("command")?.Value, true, out WallCommand command))
                        payload.Command = command;

                    foreach (var e in root.Elements("Camera"))
                    {
                        payload.Cameras.Add(new WallCameraRef
                        {
                            Id = PaneDefinition.ParseGuid(e.Attribute("id")?.Value),
                            Name = e.Attribute("name")?.Value ?? ""
                        });
                    }

                    return payload;
                }
                catch
                {
                    return null;
                }
            }

            /// <summary>A line for the log that says what the wall was told to do.</summary>
            public string Describe()
            {
                switch (Command)
                {
                    case WallCommand.Clear:
                        return "clear the wall";
                    case WallCommand.PlaceAt:
                        return $"show '{Cameras.FirstOrDefault()?.Name}' at position {Position}";
                    default:
                        return $"show {Cameras.Count} camera(s)";
                }
            }
        }

        public sealed class SetWallViewPayload
        {
            public string UserName { get; set; } = "";
            public Guid ViewProfileId { get; set; }
            public string ViewProfileName { get; set; } = "";
            public DateTime SentUtc { get; set; } = DateTime.UtcNow;

            public string ToXml() => new XElement("SetWallView",
                new XAttribute("user", UserName ?? ""),
                new XAttribute("viewProfileId", ViewProfileId),
                new XAttribute("viewProfileName", ViewProfileName ?? ""),
                new XAttribute("sentUtc", SentUtc.ToString("O", CultureInfo.InvariantCulture))).ToString();

            public static SetWallViewPayload FromXml(string xml)
            {
                if (string.IsNullOrWhiteSpace(xml)) return null;
                try
                {
                    var root = XElement.Parse(xml);
                    return new SetWallViewPayload
                    {
                        UserName = root.Attribute("user")?.Value ?? "",
                        ViewProfileId = PaneDefinition.ParseGuid(root.Attribute("viewProfileId")?.Value),
                        ViewProfileName = root.Attribute("viewProfileName")?.Value ?? "",
                        SentUtc = DateTime.TryParse(root.Attribute("sentUtc")?.Value,
                            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t)
                            ? t : DateTime.UtcNow
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
