using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        // Range-scan for the LineChart playback backfill. Runs on a fresh
        // MetadataPlaybackSource off the UI thread so it doesn't fight the live
        // pump for the same source. Returns extracted (value, timestamp) pairs
        // covering [from, to] in chronological order.
        //
        // We use MetadataPlaybackSource.Get(timestamp, maxTimeAfter, maxCount):
        // it returns up to maxCount frames forward of `timestamp`. We loop with
        // the last frame's NextDateTime as the next anchor until we pass `to`
        // or the server stops returning data.
        public Task<List<(string Value, DateTime TimestampUtc)>> ScanRangeAsync(
            DateTime fromUtc, DateTime toUtc, int maxFrames, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var results = new List<(string, DateTime)>();
                if (toUtc <= fromUtc) return results;
                if (fromUtc.Kind != DateTimeKind.Utc) fromUtc = fromUtc.ToUniversalTime();
                if (toUtc.Kind   != DateTimeKind.Utc) toUtc   = toUtc.ToUniversalTime();

                MetadataPlaybackSource src = null;
                try
                {
                    src = new MetadataPlaybackSource(_item);
                    src.Init();

                    var cfg = _cfgProvider("playback-range");
                    if (cfg == null || string.IsNullOrEmpty(cfg.DataKey)) return results;

                    var cursor = fromUtc;
                    int chunk = 200;
                    int safety = 0;
                    while (!ct.IsCancellationRequested && cursor <= toUtc && safety++ < 1000)
                    {
                        List<MetadataPlaybackData> frames = null;
                        try
                        {
                            frames = src.Get(cursor, toUtc - cursor, chunk);
                        }
                        catch (Exception ex)
                        {
                            _log.Error($"[Pump] ScanRange Get threw: {ex.Message}");
                            break;
                        }
                        if (frames == null || frames.Count == 0) break;

                        DateTime? lastFrameUtc = null;
                        foreach (var f in frames)
                        {
                            if (f == null) continue;
                            string xml;
                            try { xml = f.Content?.GetMetadataString(); }
                            catch { continue; }
                            if (string.IsNullOrEmpty(xml)) continue;

                            var hit = MetadataExtractor.TryExtract(xml, cfg);
                            if (hit != null) results.Add((hit.Value, hit.TimestampUtc));
                            lastFrameUtc = f.DateTime;

                            if (results.Count >= maxFrames) return results;
                        }

                        if (!lastFrameUtc.HasValue) break;
                        // Step past the last frame to avoid re-fetching it. NextDateTime
                        // would be more correct but isn't always populated; +1 tick is
                        // safe given metadata cadence is well above ticks resolution.
                        var nextStart = lastFrameUtc.Value.AddTicks(1);
                        if (nextStart <= cursor) break; // no progress
                        cursor = nextStart;
                    }
                }
                catch (Exception ex)
                {
                    _log.Error($"[Pump] ScanRange failed: {ex.Message}");
                }
                finally
                {
                    SafeClose(src);
                }
                return results;
            }, ct);
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
