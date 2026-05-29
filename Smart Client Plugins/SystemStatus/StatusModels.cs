using System;
using System.Collections.Generic;

namespace SystemStatus
{
    /// <summary>One enabled camera and whether the recorder currently reports it online.</summary>
    public sealed class CameraRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public bool Online { get; set; }

        // Display-ready so the XAML needs no value converters.
        public string StatusText => Online ? "Online" : "Offline";
    }

    /// <summary>One connected user (after services are filtered out and duplicates removed).</summary>
    public sealed class UserRow
    {
        public string DisplayName { get; set; }
        // Display-ready secondary line: the client type (e.g. "Smart Client") when known,
        // otherwise a cleaned location. Never a raw IPv6 link-local address.
        public string Secondary { get; set; }
    }

    /// <summary>
    /// Immutable view of system status produced by the background plugin and consumed by the
    /// toolbar label and the flyout. Counts are precomputed so consumers do no work.
    /// </summary>
    public sealed class StatusSnapshot
    {
        public static readonly StatusSnapshot Empty =
            new StatusSnapshot(new List<CameraRow>(), new List<UserRow>(), 0, 0);

        public StatusSnapshot(IReadOnlyList<CameraRow> cameras, IReadOnlyList<UserRow> users,
                              int onlineCount, int enabledCount)
        {
            Cameras = cameras;
            Users = users;
            OnlineCount = onlineCount;
            EnabledCount = enabledCount;
        }

        public IReadOnlyList<CameraRow> Cameras { get; }
        public IReadOnlyList<UserRow> Users { get; }
        public int OnlineCount { get; }
        public int EnabledCount { get; }
        public int UserCount => Users.Count;

        public string ToolbarText => $"{OnlineCount}/{EnabledCount} Cameras   {UserCount} Users";
    }

    public sealed class StatusChangedEventArgs : EventArgs
    {
        public StatusChangedEventArgs(StatusSnapshot snapshot) { Snapshot = snapshot; }
        public StatusSnapshot Snapshot { get; }
    }
}
