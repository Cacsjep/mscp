using System;
using VideoOS.Platform;

namespace BarcodeReader.Background
{
    /// <summary>
    /// Strongly-typed view over an Item's string-dictionary properties.
    /// Shared by BackgroundPlugin (when launching helpers) and the Admin UI (when
    /// reading/writing values), so property keys live in exactly one place.
    /// </summary>
    internal class ChannelConfig
    {
        public const string KeyEnabled        = "Enabled";
        public const string KeyCameraId       = "CameraId";
        public const string KeyCameraName     = "CameraName";
        public const string KeyFormats        = "Formats";
        public const string KeyTryHarder      = "TryHarder";
        public const string KeyAutoRotate     = "AutoRotate";
        public const string KeyTryInverted    = "TryInverted";
        public const string KeyTargetFps      = "TargetFps";
        public const string KeyDownscaleWidth = "DownscaleWidth";
        public const string KeyDebounceMs     = "DebounceMs";
        public const string KeyCreateBookmarks = "CreateBookmarks";

        public static readonly string[] PropertyKeys =
        {
            KeyEnabled, KeyCameraId, KeyCameraName, KeyFormats,
            KeyTryHarder, KeyAutoRotate, KeyTryInverted,
            KeyTargetFps, KeyDownscaleWidth, KeyDebounceMs,
            KeyCreateBookmarks
        };

        // Defaults match the QrReader sample at G:\qrcode for consistency.
        public const string DefaultFormats = "qr,data_matrix,aztec,pdf417,code128,code39,code93,ean13,ean8,upca,upce,itf,codabar";
        public const int    DefaultTargetFps = 1;
        public const int    DefaultDownscaleWidth = 0;   // 0 = native
        public const int    DefaultDebounceMs = 2000;

        public Guid   ItemId;
        public string Name;
        public bool   Enabled;
        public Guid   CameraId;
        public string CameraName;
        public string Formats;
        public bool   TryHarder;
        public bool   AutoRotate;
        public bool   TryInverted;
        public int    TargetFps;
        public int    DownscaleWidth;
        public int    DebounceMs;
        public bool   CreateBookmarks;

        public static ChannelConfig FromItem(Item item)
        {
            var p = item.Properties;

            // Default to enabled unless explicitly set to "No" so newly-created items
            // start streaming without the user having to toggle Enabled first.
            var enabled = !p.ContainsKey(KeyEnabled) || p[KeyEnabled] != "No";
            Guid.TryParse(p.ContainsKey(KeyCameraId) ? p[KeyCameraId] : "", out var camId);

            return new ChannelConfig
            {
                ItemId         = item.FQID.ObjectId,
                Name           = item.Name ?? "",
                Enabled        = enabled,
                CameraId       = camId,
                CameraName     = p.ContainsKey(KeyCameraName)     ? p[KeyCameraName] : "",
                Formats        = p.ContainsKey(KeyFormats)        ? p[KeyFormats] : DefaultFormats,
                TryHarder      = GetBool(p, KeyTryHarder,   defaultValue: true),
                AutoRotate     = GetBool(p, KeyAutoRotate,  defaultValue: true),
                TryInverted    = GetBool(p, KeyTryInverted, defaultValue: false),
                TargetFps       = GetInt(p,  KeyTargetFps,      DefaultTargetFps,      min: 1, max: 60),
                DownscaleWidth  = GetInt(p,  KeyDownscaleWidth, DefaultDownscaleWidth, min: 0, max: 7680),
                DebounceMs      = GetInt(p,  KeyDebounceMs,     DefaultDebounceMs,     min: 0, max: 60000),
                CreateBookmarks = GetBool(p, KeyCreateBookmarks, defaultValue: true),
            };
        }

        private static bool GetBool(System.Collections.Generic.Dictionary<string, string> p, string key, bool defaultValue)
        {
            if (!p.ContainsKey(key)) return defaultValue;
            return p[key] == "Yes";
        }

        private static int GetInt(System.Collections.Generic.Dictionary<string, string> p, string key, int defaultValue, int min, int max)
        {
            if (!p.ContainsKey(key) || !int.TryParse(p[key], out var v)) return defaultValue;
            if (v < min) v = min;
            if (v > max) v = max;
            return v;
        }
    }
}
