using System;
using VideoOS.Platform;
using VideoOS.Platform.Client;
using VideoOS.Platform.Messaging;

namespace TimelineJump.Client
{
    internal enum JumpResult
    {
        MasterPlayback,
        IndependentPlayback,
        SwitchedToIndependent,
        NoSelection,
        Failed,
    }

    internal static class PlaybackJumper
    {
        /// <summary>
        /// Jump the timeline by the given offset.
        /// - In Playback mode: moves the master timeline (entire view) via PlaybackCommand.Goto.
        /// - In Live mode (or when a tile is already in independent playback): moves the
        ///   selected tile's independent playback time. If live, switches the tile to
        ///   independent playback first (mirrors the SDK Rewind15Seconds sample).
        /// </summary>
        public static JumpResult JumpBy(TimeSpan delta, out string detail)
        {
            detail = null;
            try
            {
                var addon = ImageViewerHelper.GetGlobalSelectedImageViewer();

                // Case 1: tile is already in independent playback - just shift its time.
                if (addon != null && addon.IndependentPlaybackEnabled
                    && addon.IndependentPlaybackController != null)
                {
                    var current = addon.IndependentPlaybackController.PlaybackTime;
                    var target = current.Add(delta);
                    addon.IndependentPlaybackController.PlaybackTime = target;
                    detail = $"Independent: {target.ToLocalTime():HH:mm:ss.fff}";
                    return JumpResult.IndependentPlayback;
                }

                var mode = EnvironmentManager.Instance.Mode;

                // Case 2: master playback mode - shift the entire view's timeline.
                if (mode == Mode.ClientPlayback)
                {
                    var current = GetCurrentMasterPlaybackTime();
                    var target = current.Add(delta);
                    EnvironmentManager.Instance.PostMessage(new Message(
                        MessageId.SmartClient.PlaybackCommand,
                        new PlaybackCommandData
                        {
                            Command = PlaybackData.Goto,
                            DateTime = target
                        }));
                    detail = $"Master: {target.ToLocalTime():HH:mm:ss.fff}";
                    return JumpResult.MasterPlayback;
                }

                // Case 3: live mode - switch the selected tile to independent playback
                // anchored at (now + delta). Same approach the SDK Rewind15Seconds sample uses.
                if (addon == null)
                {
                    detail = "Select a camera tile first.";
                    return JumpResult.NoSelection;
                }

                var liveAnchor = DateTime.UtcNow.Add(delta);
                addon.IndependentPlaybackEnabled = true;
                if (addon.IndependentPlaybackController != null)
                    addon.IndependentPlaybackController.PlaybackTime = liveAnchor;
                detail = $"Switched to independent playback: {liveAnchor.ToLocalTime():HH:mm:ss.fff}";
                return JumpResult.SwitchedToIndependent;
            }
            catch (Exception ex)
            {
                TimelineJumpDefinition.Log.Error("JumpBy failed", ex);
                detail = ex.Message;
                return JumpResult.Failed;
            }
        }

        public static DateTime? TryGetCurrentTime()
        {
            try
            {
                var addon = ImageViewerHelper.GetGlobalSelectedImageViewer();
                if (addon != null && addon.IndependentPlaybackEnabled
                    && addon.IndependentPlaybackController != null)
                {
                    return addon.IndependentPlaybackController.PlaybackTime;
                }

                if (EnvironmentManager.Instance.Mode == Mode.ClientPlayback)
                    return GetCurrentMasterPlaybackTime();

                return null; // Live - no meaningful "current time"
            }
            catch
            {
                return null;
            }
        }

        public static string GetSelectedCameraName()
        {
            try
            {
                var addon = ImageViewerHelper.GetGlobalSelectedImageViewer();
                if (addon?.CameraFQID == null) return null;
                var item = Configuration.Instance.GetItem(addon.CameraFQID);
                return item?.Name;
            }
            catch
            {
                return null;
            }
        }

        private static DateTime GetCurrentMasterPlaybackTime()
        {
            var responses = EnvironmentManager.Instance.SendMessage(
                new Message(MessageId.SmartClient.GetCurrentPlaybackTimeRequest));
            if (responses != null)
            {
                foreach (var r in responses)
                {
                    if (r is DateTime dt && dt != DateTime.MinValue)
                        return dt;
                }
            }
            return DateTime.UtcNow;
        }
    }
}
