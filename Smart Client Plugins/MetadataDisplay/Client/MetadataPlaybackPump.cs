using System;
using System.Threading;
using CommunitySDK;
using VideoOS.Platform;
using VideoOS.Platform.Data;

namespace MetadataDisplay.Client
{
    // Worker that owns a MetadataPlaybackSource and fetches the metadata frame
    // at-or-before a requested UTC playhead time. Called by the Smart Client
    // ViewItem WPF when ClientPlayback mode is active.
    //
    // Pattern mirrors the official Milestone MetadataPlaybackViewer sample:
    //   - All MetadataPlaybackSource calls happen on a dedicated worker thread.
    //   - GetAtOrBefore(utc) is the primary fetch.
    //   - Window-skip optimization: if requested time falls inside the last
    //     fetched frame's [PreviousDateTime, NextDateTime] range, skip the
    //     re-fetch; the value didn't change.
    //   - On CommunicationMIPException, drop the source and reinit on next loop.
    internal sealed class MetadataPlaybackPump
    {
        private static readonly PluginLog _log = new PluginLog("MetadataDisplay");

        private readonly Item _item;
        private readonly Action<string, DateTime> _onValue;        // value, ts (UI thread caller marshals)
        private readonly Func<string, ExtractorConfig> _cfgProvider; // pulls latest config (so a Save while in playback updates next fetch)

        private Thread _thread;
        private volatile bool _stop;
        private readonly ManualResetEventSlim _wake = new ManualResetEventSlim(false);
        private long _requestedUtcTicks;        // 0 = no pending request
        private DateTime _lastFrameUtc, _lastPrevUtc, _lastNextUtc;
        private bool _haveLastWindow;
        private string _lastValue;

        public MetadataPlaybackPump(Item item, Func<string, ExtractorConfig> cfgProvider, Action<string, DateTime> onValue)
        {
            _item = item;
            _cfgProvider = cfgProvider;
            _onValue = onValue;
        }

        public void Start()
        {
            if (_thread != null) return;
            _stop = false;
            _thread = new Thread(Run) { IsBackground = true, Name = "MetadataDisplay.PlaybackPump" };
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
            _wake.Set();
            try { _thread?.Join(2000); } catch { }
            _thread = null;
        }

        public void RequestTime(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                utc = utc.ToUniversalTime();
            Interlocked.Exchange(ref _requestedUtcTicks, utc.Ticks);
            _wake.Set();
        }

        private void Run()
        {
            MetadataPlaybackSource source = null;
            bool needReinit = true;

            while (!_stop)
            {
                if (needReinit)
                {
                    SafeClose(source); source = null;
                    try
                    {
                        _log.Info($"[Pump] PlaybackSource Init for '{_item?.Name}'");
                        source = new MetadataPlaybackSource(_item);
                        source.Init();
                        _log.Info("[Pump] PlaybackSource Init OK");
                        needReinit = false;
                        _haveLastWindow = false;
                    }
                    catch (Exception ex)
                    {
                        _log.Error($"[Pump] PlaybackSource Init failed: {ex.Message}");
                        Thread.Sleep(2000);
                        continue;
                    }
                }

                _wake.Reset();
                long ticks = Interlocked.Exchange(ref _requestedUtcTicks, 0);
                if (ticks == 0)
                {
                    _wake.Wait(1000);
                    continue;
                }

                var requested = new DateTime(ticks, DateTimeKind.Utc);

                // Skip if the requested time still falls inside the last fetched frame's window.
                if (_haveLastWindow && requested >= _lastPrevUtc && requested < _lastNextUtc)
                    continue;

                MetadataPlaybackData data = null;
                try { data = source.GetAtOrBefore(requested); }
                catch (CommunicationMIPException ex)
                {
                    _log.Error($"GetAtOrBefore CommunicationMIPException: {ex.Message}");
                    needReinit = true;
                    continue;
                }
                catch (Exception ex)
                {
                    _log.Error($"GetAtOrBefore threw: {ex.Message}");
                    Thread.Sleep(500);
                    continue;
                }

                if (data == null) continue;

                _lastFrameUtc = data.DateTime;
                _lastPrevUtc = data.PreviousDateTime ?? _lastFrameUtc.AddMilliseconds(-1);
                _lastNextUtc = data.NextDateTime ?? _lastFrameUtc.AddMilliseconds(1);
                _haveLastWindow = true;

                string xml;
                try { xml = data.Content?.GetMetadataString(); }
                catch (Exception ex) { _log.Error($"GetMetadataString threw: {ex.Message}"); continue; }
                if (string.IsNullOrEmpty(xml)) continue;

                ExtractorConfig cfg;
                try { cfg = _cfgProvider("playback"); }
                catch { continue; }
                if (cfg == null || string.IsNullOrEmpty(cfg.DataKey)) continue;

                var hit = MetadataExtractor.TryExtract(xml, cfg);
                if (hit == null) continue;

                _lastValue = hit.Value;
                try { _onValue?.Invoke(hit.Value, hit.TimestampUtc); }
                catch (Exception ex) { _log.Error($"onValue threw: {ex.Message}"); }
            }

            SafeClose(source);
        }

        private static void SafeClose(MetadataPlaybackSource s)
        {
            if (s == null) return;
            try { s.Close(); } catch { }
        }
    }
}
