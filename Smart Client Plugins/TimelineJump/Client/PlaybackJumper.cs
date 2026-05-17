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
        NoSelection,
        Failed,
    }

    internal static class PlaybackJumper
    {
        /// <summary>
        /// Jump the timeline by the given offset.
        /// - When the selected tile (or any tracked tile) is in independent playback:
        ///   moves only that tile, snapped to its nearest recording.
        /// - Otherwise in Playback workspace: moves the master timeline, snapped to
        ///   the selected tile's nearest recording when possible.
        /// - Otherwise: returns NoSelection - the toolbar should already be disabled.
        /// </summary>
        public static JumpResult JumpBy(TimeSpan delta, out string detail)
        {
            detail = null;
            try
            {
                // Prefer the globally-selected tile, but fall back to the first tile
                // already in independent playback. Opening the WPF flyout can clear
                // the Smart Client global selection on a tile, in which case
                // GetGlobalSelectedImageViewer() returns null even though the
                // operator clearly meant to act on the independent-playback tile.
                var addon = ImageViewerHelper.GetGlobalSelectedImageViewer()
                            ?? ImageViewerHelper.GetFirstIndependentPlayback();

                // Case 1: tile is already in independent playback - just shift its time.
                if (addon != null && addon.IndependentPlaybackEnabled
                    && addon.IndependentPlaybackController != null)
                {
                    var current = addon.IndependentPlaybackController.PlaybackTime;
                    var rawTarget = current.Add(delta);
                    var cameraItem = addon.CameraFQID != null
                        ? Configuration.Instance.GetItem(addon.CameraFQID)
                        : null;
                    var target = RecordingSnap.SnapToRecording(cameraItem, rawTarget, delta);
                    addon.IndependentPlaybackController.PlaybackTime = target;
                    var snapNote = target == rawTarget ? "" : " (snapped)";
                    detail = $"Independent: {target.ToLocalTime():HH:mm:ss.fff}{snapNote}";
                    return JumpResult.IndependentPlayback;
                }

                var mode = EnvironmentManager.Instance.Mode;

                // Case 2: master playback mode - shift the entire view's timeline.
                // If we have a selected tile, snap to its recording so an empty-region
                // chip click moves visibly. With no selection we send the raw target;
                // master timeline can't be snapped per-camera since each tile has its
                // own coverage.
                if (mode == Mode.ClientPlayback)
                {
                    var current = GetCurrentMasterPlaybackTime();
                    var rawTarget = current.Add(delta);
                    var target = rawTarget;
                    if (addon != null && addon.CameraFQID != null)
                    {
                        var cameraItem = Configuration.Instance.GetItem(addon.CameraFQID);
                        target = RecordingSnap.SnapToRecording(cameraItem, rawTarget, delta);
                    }
                    EnvironmentManager.Instance.PostMessage(new Message(
                        MessageId.SmartClient.PlaybackCommand,
                        new PlaybackCommandData
                        {
                            Command = PlaybackData.Goto,
                            DateTime = target
                        }));
                    var snapNote = target == rawTarget ? "" : " (snapped)";
                    detail = $"Master: {target.ToLocalTime():HH:mm:ss.fff}{snapNote}";
                    return JumpResult.MasterPlayback;
                }

                // Case 3: live mode without any independent-playback tile. The toolbar
                // button should already be disabled in this state, so this is a
                // belt-and-suspenders branch.
                detail = "Put a camera into independent playback first.";
                return JumpResult.NoSelection;
            }
            catch (Exception ex)
            {
                TimelineJumpDefinition.Log.Error("JumpBy failed", ex);
                detail = ex.Message;
                return JumpResult.Failed;
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
