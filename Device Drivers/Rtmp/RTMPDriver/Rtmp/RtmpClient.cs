using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using VideoOS.Platform.DriverFramework.Utilities;

namespace RTMPDriver.Rtmp
{
    /// <summary>
    /// Handles a single RTMP client connection on a dedicated thread.
    /// Performs handshake, chunk stream parsing, AMF command handling,
    /// and FLV video tag → Annex B H.264 extraction.
    /// </summary>
    internal class RtmpClient
    {
        private const int HandshakeSize = 1536;
        private const int DefaultChunkSize = 128;
        private const int OutputChunkSize = 4096;
        private const int PublishTimeoutMs = 10000;

        private readonly TcpClient _tcpClient;
        private readonly Func<string, RtmpStreamBuffer> _bufferLookup;
        private readonly CancellationToken _shutdownToken;
        private readonly string _remoteEndPoint;

        private Stream _stream;
        private int _inChunkSize = DefaultChunkSize;
        private int _outChunkSize = DefaultChunkSize;
        private readonly Dictionary<int, ChunkStreamContext> _chunkStreams = new Dictionary<int, ChunkStreamContext>();

        private RtmpStreamBuffer _activeBuffer;
        private string _publishPath;
        private string _appName; // from connect command (e.g., "live" or "stream1")
        private int _naluLengthSize = 4; // from AVCDecoderConfigurationRecord
        private DateTime _rtmpEpoch; // wall-clock time corresponding to RTMP timestamp 0
        private Timer _publishTimer;
        private Timer _videoDataTimer;

        public RtmpClient(TcpClient tcpClient, Stream stream, Func<string, RtmpStreamBuffer> bufferLookup, CancellationToken shutdownToken)
        {
            _tcpClient = tcpClient;
            _stream = stream;
            _bufferLookup = bufferLookup;
            _shutdownToken = shutdownToken;
            _remoteEndPoint = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).ToString();
        }

        public void Run()
        {
            try
            {
                PerformHandshake();
                Toolbox.Log.Trace("RtmpClient: Handshake completed with {0}", _remoteEndPoint);

                _publishTimer = new Timer(_ =>
                {
                    if (_activeBuffer == null)
                    {
                        Toolbox.Log.Trace("RtmpClient: {0} publish timeout ({1}ms), disconnecting", _remoteEndPoint, PublishTimeoutMs);
                        try { _tcpClient.Close(); } catch { }
                    }
                }, null, PublishTimeoutMs, Timeout.Infinite);

                while (!_shutdownToken.IsCancellationRequested)
                {
                    var msg = ReadMessage();
                    if (msg == null) break;
                    HandleMessage(msg);
                }
            }
            catch (IOException ex)
            {
                Toolbox.Log.Trace("RtmpClient: {0} IO disconnect: {1}", _remoteEndPoint, ex.Message);
            }
            catch (ObjectDisposedException)
            {
                // Socket closed during shutdown - expected
            }
            catch (Exception ex)
            {
                Toolbox.Log.LogError("RtmpClient", "Error from {0}: {1}\n{2}", _remoteEndPoint, ex.Message, ex.StackTrace);
            }
            finally
            {
                try { _publishTimer?.Dispose(); } catch { }
                try { _videoDataTimer?.Dispose(); } catch { }
                if (_activeBuffer != null)
                {
                    _activeBuffer.SetOffline();
                    Toolbox.Log.Trace("RtmpClient: Stream '{0}' went offline (client {1})", _publishPath, _remoteEndPoint);
                }
                try { _tcpClient.Close(); } catch { }
                Toolbox.Log.Trace("RtmpClient: Disconnected {0}", _remoteEndPoint);
            }
        }

        #region Handshake

        private void PerformHandshake()
        {
            // Read C0 (1 byte) + C1 (1536 bytes)
            byte c0 = ReadByte();
            if (c0 != 3)
                throw new InvalidDataException($"Unsupported RTMP version: {c0}");

            byte[] c1 = ReadBytes(HandshakeSize);

            // Send S0 + S1 + S2
            _stream.WriteByte(3); // S0

            byte[] s1 = new byte[HandshakeSize];
            var rng = new Random();
            rng.NextBytes(s1);
            // Timestamp = 0
            s1[0] = 0; s1[1] = 0; s1[2] = 0; s1[3] = 0;
            // Zero
            s1[4] = 0; s1[5] = 0; s1[6] = 0; s1[7] = 0;
            _stream.Write(s1, 0, HandshakeSize); // S1

            // S2 = echo of C1
            _stream.Write(c1, 0, HandshakeSize); // S2
            _stream.Flush();

            // Read C2 (1536 bytes) - content not validated per spec
            ReadBytes(HandshakeSize);
        }

        #endregion

        #region Chunk Stream Reading

        private RtmpMessage ReadMessage()
        {
            while (!_shutdownToken.IsCancellationRequested)
            {
                // Read basic header
                int firstByte = _stream.ReadByte();
                if (firstByte < 0) return null;

                int fmt = (firstByte >> 6) & 0x03;
                int csid = firstByte & 0x3F;

                if (csid == 0)
                {
                    csid = ReadByte() + 64;
                }
                else if (csid == 1)
                {
                    int b0 = ReadByte();
                    int b1 = ReadByte();
                    csid = b1 * 256 + b0 + 64;
                }

                // Get or create chunk stream context
                if (!_chunkStreams.TryGetValue(csid, out var ctx))
                {
                    if (_chunkStreams.Count >= Constants.MaxChunkStreamsPerClient)
                        throw new InvalidDataException($"Too many chunk streams ({_chunkStreams.Count})");
                    ctx = new ChunkStreamContext();
                    _chunkStreams[csid] = ctx;
                }

                // Read message header based on fmt
                uint timestamp = 0;
                bool hasExtendedTimestamp = false;

                if (fmt == 0)
                {
                    timestamp = ReadUInt24();
                    ctx.MessageLength = (int)ReadUInt24();
                    if (ctx.MessageLength < 0 || ctx.MessageLength > Constants.MaxMessageSize)
                        throw new InvalidDataException($"Message length {ctx.MessageLength} exceeds limit of {Constants.MaxMessageSize}");
                    ctx.MessageTypeId = ReadByte();
                    ctx.MessageStreamId = ReadUInt32LE();
                    hasExtendedTimestamp = timestamp == 0xFFFFFF;
                    if (!hasExtendedTimestamp)
                        ctx.Timestamp = timestamp;
                }
                else if (fmt == 1)
                {
                    timestamp = ReadUInt24();
                    ctx.MessageLength = (int)ReadUInt24();
                    if (ctx.MessageLength < 0 || ctx.MessageLength > Constants.MaxMessageSize)
                        throw new InvalidDataException($"Message length {ctx.MessageLength} exceeds limit of {Constants.MaxMessageSize}");
                    ctx.MessageTypeId = ReadByte();
                    hasExtendedTimestamp = timestamp == 0xFFFFFF;
                    if (!hasExtendedTimestamp)
                        ctx.TimestampDelta = timestamp;
                }
                else if (fmt == 2)
                {
                    timestamp = ReadUInt24();
                    hasExtendedTimestamp = timestamp == 0xFFFFFF;
                    if (!hasExtendedTimestamp)
                        ctx.TimestampDelta = timestamp;
                }
                // fmt == 3: all inherited

                if (hasExtendedTimestamp)
                {
                    uint extTs = ReadUInt32BE();
                    if (fmt == 0)
                        ctx.Timestamp = extTs;
                    else
                        ctx.TimestampDelta = extTs;
                }

                // Apply timestamp delta for fmt 1, 2, 3
                if (fmt != 0)
                {
                    ctx.Timestamp += ctx.TimestampDelta;
                }

                // Allocate buffer if starting a new message
                if (ctx.BytesRead == 0)
                {
                    ctx.MessageBuffer = new byte[ctx.MessageLength];
                }

                // Read chunk data
                int remaining = ctx.MessageLength - ctx.BytesRead;
                int toRead = Math.Min(remaining, _inChunkSize);
                ReadBytesInto(ctx.MessageBuffer, ctx.BytesRead, toRead);
                ctx.BytesRead += toRead;

                // If message is complete, return it
                if (ctx.BytesRead >= ctx.MessageLength)
                {
                    var msg = new RtmpMessage
                    {
                        TypeId = ctx.MessageTypeId,
                        StreamId = ctx.MessageStreamId,
                        Timestamp = ctx.Timestamp,
                        Data = ctx.MessageBuffer
                    };
                    ctx.BytesRead = 0;
                    ctx.MessageBuffer = null;
                    return msg;
                }
            }
            return null;
        }

        #endregion

        #region Message Handling

        private void HandleMessage(RtmpMessage msg)
        {
            switch (msg.TypeId)
            {
                case 1: // Set Chunk Size
                    if (msg.Data.Length >= 4)
                    {
                        _inChunkSize = (int)ReadUInt32BE(msg.Data, 0) & 0x7FFFFFFF;
                        if (_inChunkSize < Constants.MinChunkSize || _inChunkSize > Constants.MaxChunkSize)
                            throw new InvalidDataException($"Invalid chunk size {_inChunkSize} (valid range: {Constants.MinChunkSize}-{Constants.MaxChunkSize})");
                        Toolbox.Log.Trace("RtmpClient: {0} set chunk size to {1}", _remoteEndPoint, _inChunkSize);
                    }
                    break;

                case 3: // Acknowledgement
                    break; // Ignore

                case 5: // Window Acknowledgement Size
                    break; // Ignore

                case 6: // Set Peer Bandwidth
                    break; // Ignore

                case 8: // Audio
                    break; // Ignored - we only handle video

                case 9: // Video
                    HandleVideoData(msg.Data, msg.Timestamp);
                    break;

                case 15: // AMF3 Data
                    break; // Ignore

                case 17: // AMF3 Command - strip leading 0x00 byte, then parse as AMF0
                    if (msg.Data.Length > 1)
                    {
                        var amf3Msg = new RtmpMessage
                        {
                            TypeId = 20,
                            StreamId = msg.StreamId,
                            Timestamp = msg.Timestamp,
                            Data = new byte[msg.Data.Length - 1]
                        };
                        Array.Copy(msg.Data, 1, amf3Msg.Data, 0, msg.Data.Length - 1);
                        HandleAmfCommand(amf3Msg);
                    }
                    break;

                case 18: // AMF0 Data (metadata like @setDataFrame)
                    break; // Ignore

                case 20: // AMF0 Command
                    HandleAmfCommand(msg);
                    break;

                default:
                    Toolbox.Log.Trace("RtmpClient: {0} unhandled message type {1}", _remoteEndPoint, msg.TypeId);
                    break;
            }
        }

        #endregion

        #region AMF Command Handling

        private void HandleAmfCommand(RtmpMessage msg)
        {
            var args = Amf0Reader.ParseCommand(msg.Data);
            if (args.Count < 2) return;

            string command = args[0] as string;
            double txId = args.Count > 1 && args[1] is double d ? d : 0;

            switch (command)
            {
                case "connect":
                    HandleConnect(txId, args);
                    break;
                case "releaseStream":
                case "FCPublish":
                    SendAmfResult(txId, null, null);
                    break;
                case "createStream":
                    HandleCreateStream(txId);
                    break;
                case "publish":
                    HandlePublish(txId, args);
                    break;
                case "FCUnpublish":
                case "deleteStream":
                    HandleDeleteStream();
                    break;
                default:
                    Toolbox.Log.Trace("RtmpClient: {0} unhandled command '{1}'", _remoteEndPoint, Sanitize(command));
                    break;
            }
        }

        private void HandleConnect(double txId, List<object> args)
        {
            // Extract app name from connect command object
            if (args.Count > 2 && args[2] is Dictionary<string, object> cmdObj)
            {
                if (cmdObj.TryGetValue("app", out object appVal) && appVal is string appStr)
                {
                    _appName = Sanitize(appStr.Trim('/'));
                    Toolbox.Log.Trace("RtmpClient: {0} app='{1}'", _remoteEndPoint, _appName);
                }
            }

            // Send Window Acknowledgement Size
            SendProtocolMessage(5, WriteUInt32BE(5000000));
            // Send Set Peer Bandwidth
            var bw = new byte[5];
            WriteUInt32BE(bw, 0, 5000000);
            bw[4] = 2; // Dynamic
            SendProtocolMessage(6, bw);
            // Send Set Chunk Size
            _outChunkSize = OutputChunkSize;
            SendProtocolMessage(1, WriteUInt32BE((uint)_outChunkSize));
            // Send Stream Begin (user control message type 0)
            var streamBegin = new byte[6];
            // Event type 0 = Stream Begin
            streamBegin[0] = 0; streamBegin[1] = 0;
            // Stream ID 0
            streamBegin[2] = 0; streamBegin[3] = 0; streamBegin[4] = 0; streamBegin[5] = 0;
            SendProtocolMessage(4, streamBegin);

            // Send _result
            var props = new Dictionary<string, object>
            {
                {"fmsVer", "FMS/3,5,3,888"},
                {"capabilities", 31.0}
            };
            var info = new Dictionary<string, object>
            {
                {"level", "status"},
                {"code", "NetConnection.Connect.Success"},
                {"description", "Connection succeeded."},
                {"objectEncoding", 0.0}
            };
            SendAmfResult(txId, props, info);
            Toolbox.Log.Trace("RtmpClient: {0} connected", _remoteEndPoint);
        }

        private void HandleCreateStream(double txId)
        {
            SendAmfResult(txId, null, 1.0);
        }

        private void HandlePublish(double txId, List<object> args)
        {
            // args: ["publish", txId, null, streamName, publishType]
            string streamName = Sanitize(args.Count > 3 ? args[3] as string : null);

            // Build the full stream path from app name + stream name
            // FFmpeg URL: rtmp://host:port/app/streamName
            //   - connect sends app="app"
            //   - publish sends streamName="streamName"
            // FFmpeg URL: rtmp://host:port/stream1  (no stream name)
            //   - connect sends app="stream1"
            //   - publish sends streamName="" or null
            string fullPath;
            if (!string.IsNullOrEmpty(streamName))
            {
                // Has both app and stream name: combine them
                // But if app name equals stream name (e.g., rtmp://host/stream1/stream1), just use stream name
                if (!string.IsNullOrEmpty(_appName) && _appName != streamName)
                    fullPath = "/" + _appName + "/" + streamName.TrimStart('/');
                else
                    fullPath = "/" + streamName.TrimStart('/');
            }
            else if (!string.IsNullOrEmpty(_appName))
            {
                // No stream name, use app name as stream path
                fullPath = "/" + _appName.TrimStart('/');
            }
            else
            {
                Toolbox.Log.LogError("RtmpClient", "{0} publish with no stream name or app name", _remoteEndPoint);
                return;
            }

            _publishPath = fullPath;

            var buffer = _bufferLookup(_publishPath);
            if (buffer == null)
            {
                Toolbox.Log.Trace("RtmpClient: {0} rejected, no active device for path '{1}'", _remoteEndPoint, _publishPath);
                var error = new Dictionary<string, object>
                {
                    {"level", "error"},
                    {"code", "NetStream.Publish.BadName"},
                    {"description", "Publish rejected."}
                };
                SendAmfCommand("onStatus", 0, null, error, 1);
                _publishPath = null;
                return;
            }

            if (!buffer.SetLive(_remoteEndPoint))
            {
                Toolbox.Log.Trace("RtmpClient: {0} rejected, '{1}' already has an active publisher", _remoteEndPoint, _publishPath);
                var error = new Dictionary<string, object>
                {
                    {"level", "error"},
                    {"code", "NetStream.Publish.BadName"},
                    {"description", "Publish rejected."}
                };
                SendAmfCommand("onStatus", 0, null, error, 1);
                _publishPath = null;
                return;
            }

            _activeBuffer = buffer;
            try { _publishTimer?.Dispose(); } catch { }
            _publishTimer = null;
            try { _videoDataTimer?.Dispose(); } catch { }
            _videoDataTimer = new Timer(_ =>
            {
                Toolbox.Log.Trace("RtmpClient: {0} no video data after publish, disconnecting", _remoteEndPoint);
                try { _tcpClient.Close(); } catch { }
            }, null, Constants.VideoDataTimeoutMs, Timeout.Infinite);
            Toolbox.Log.Trace("RtmpClient: {0} publishing to '{1}'", _remoteEndPoint, _publishPath);

            // Send onStatus
            var status = new Dictionary<string, object>
            {
                {"level", "status"},
                {"code", "NetStream.Publish.Start"},
                {"description", $"Publishing {streamName}"}
            };
            SendAmfCommand("onStatus", 0, null, status, 1);
        }

        private void HandleDeleteStream()
        {
            if (_activeBuffer != null)
            {
                _activeBuffer.SetOffline();
                try { _videoDataTimer?.Dispose(); } catch { }
                _videoDataTimer = null;
                Toolbox.Log.Trace("RtmpClient: {0} stopped publishing '{1}'", _remoteEndPoint, _publishPath);
                _activeBuffer = null;
                _publishPath = null;
            }
        }

        #endregion

        #region Video Data Handling

        private int _videoMsgCount;

        /// <summary>
        /// Convert RTMP timestamp (ms since stream start) to wall-clock DateTime.
        /// The epoch is set on the first video message so timestamps are properly spaced.
        /// </summary>
        private DateTime RtmpTimestampToDateTime(uint rtmpTs)
        {
            if (_rtmpEpoch == DateTime.MinValue)
                _rtmpEpoch = DateTime.UtcNow - TimeSpan.FromMilliseconds(rtmpTs);
            return _rtmpEpoch + TimeSpan.FromMilliseconds(rtmpTs);
        }

        private void HandleVideoData(byte[] payload, uint rtmpTimestamp)
        {
            if (_activeBuffer == null || payload == null || payload.Length < 2)
                return;

            _videoDataTimer?.Change(Constants.VideoDataTimeoutMs, Timeout.Infinite);
            _videoMsgCount++;
            if (_videoMsgCount <= 3)
            {
                Toolbox.Log.Trace("RtmpClient: {0} video msg #{1} len={2} rtmpTs={3} bytes[0..4]={4}",
                    _remoteEndPoint, _videoMsgCount, payload.Length, rtmpTimestamp,
                    BitConverter.ToString(payload, 0, Math.Min(5, payload.Length)));
            }

            // Check for Enhanced RTMP (bit 7 of first byte set)
            if ((payload[0] & 0x80) != 0)
            {
                HandleEnhancedVideo(payload, rtmpTimestamp);
                return;
            }

            byte frameTypeAndCodec = payload[0];
            int frameType = (frameTypeAndCodec >> 4) & 0x0F;
            int codecId = frameTypeAndCodec & 0x0F;

            if (codecId != 7) // Not AVC/H.264
            {
                Toolbox.Log.Trace("RtmpClient: {0} non-H.264 codec: {1}", _remoteEndPoint, codecId);
                return;
            }

            if (payload.Length < 5) return;

            byte avcPacketType = payload[1];
            // bytes 2-4: composition time offset (ignored for live)

            bool isKeyFrame = frameType == 1;

            DateTime frameTime = RtmpTimestampToDateTime(rtmpTimestamp);

            switch (avcPacketType)
            {
                case 0: // AVC sequence header
                    ParseAvcSequenceHeader(payload, 5);
                    break;
                case 1: // AVC NALU
                    byte[] annexB = ConvertToAnnexB(payload, 5, payload.Length - 5, isKeyFrame);
                    if (annexB != null && annexB.Length > 0)
                    {
                        _activeBuffer.PushFrame(annexB, isKeyFrame, frameTime);
                    }
                    break;
                case 2: // AVC end of sequence
                    break;
            }
        }

        /// <summary>
        /// Handle Enhanced RTMP video tags (used by OBS, modern streamers).
        /// Format: [1][frameType(3b)][packetType(4b)][FourCC(4B)][payload...]
        /// </summary>
        private void HandleEnhancedVideo(byte[] payload, uint rtmpTimestamp)
        {
            if (payload.Length < 5) return;

            int frameType = (payload[0] >> 4) & 0x07;
            int packetType = payload[0] & 0x0F;
            bool isKeyFrame = frameType == 1;

            // Read FourCC (bytes 1-4): "avc1", "hvc1", "av01", etc.
            string fourCC = new string(new char[] {
                (char)payload[1], (char)payload[2], (char)payload[3], (char)payload[4]
            });

            if (fourCC != "avc1")
            {
                Toolbox.Log.Trace("RtmpClient: {0} Enhanced RTMP unsupported codec FourCC='{1}'", _remoteEndPoint, fourCC);
                return;
            }

            DateTime frameTime = RtmpTimestampToDateTime(rtmpTimestamp);

            // Enhanced RTMP packet types:
            // 0 = SequenceStart (sequence header)
            // 1 = CodedFrames (with composition time)
            // 2 = SequenceEnd
            // 3 = CodedFramesX (no composition time)
            // 4 = Metadata
            // 5 = MPEG2TSSequenceStart

            int dataOffset;
            switch (packetType)
            {
                case 0: // SequenceStart - AVCDecoderConfigurationRecord
                    ParseAvcSequenceHeader(payload, 5);
                    Toolbox.Log.Trace("RtmpClient: {0} Enhanced RTMP sequence header received", _remoteEndPoint);
                    break;

                case 1: // CodedFrames - has 3-byte composition time offset, then NALU data
                    if (payload.Length < 8) return;
                    dataOffset = 8; // 1 (header) + 4 (FourCC) + 3 (composition time)
                    ProcessEnhancedNalus(payload, dataOffset, isKeyFrame, frameTime);
                    break;

                case 3: // CodedFramesX - no composition time, NALU data starts immediately
                    dataOffset = 5; // 1 (header) + 4 (FourCC)
                    ProcessEnhancedNalus(payload, dataOffset, isKeyFrame, frameTime);
                    break;

                case 2: // SequenceEnd
                    Toolbox.Log.Trace("RtmpClient: {0} Enhanced RTMP sequence end", _remoteEndPoint);
                    break;

                default:
                    Toolbox.Log.Trace("RtmpClient: {0} Enhanced RTMP packet type {1}", _remoteEndPoint, packetType);
                    break;
            }
        }

        private void ProcessEnhancedNalus(byte[] payload, int offset, bool isKeyFrame, DateTime frameTime)
        {
            int length = payload.Length - offset;
            if (length <= 0) return;

            byte[] annexB = ConvertToAnnexB(payload, offset, length, isKeyFrame);
            if (annexB != null && annexB.Length > 0)
            {
                _activeBuffer.PushFrame(annexB, isKeyFrame, frameTime);
            }
        }

        private void ParseAvcSequenceHeader(byte[] payload, int offset)
        {
            if (offset + 6 > payload.Length) return;

            // AVCDecoderConfigurationRecord
            // byte 0: configurationVersion
            // byte 1: AVCProfileIndication
            // byte 2: profile_compatibility
            // byte 3: AVCLevelIndication
            // byte 4: lengthSizeMinusOne (bottom 2 bits)
            _naluLengthSize = (payload[offset + 4] & 0x03) + 1;

            // byte 5: numOfSPS (bottom 5 bits)
            int numSps = payload[offset + 5] & 0x1F;
            int pos = offset + 6;

            byte[] sps = null;
            byte[] pps = null;

            for (int i = 0; i < numSps && pos + 2 <= payload.Length; i++)
            {
                int spsLen = (payload[pos] << 8) | payload[pos + 1];
                pos += 2;
                if (pos + spsLen > payload.Length) break;
                sps = new byte[spsLen];
                Array.Copy(payload, pos, sps, 0, spsLen);
                pos += spsLen;
            }

            if (pos < payload.Length)
            {
                int numPps = payload[pos] & 0xFF;
                pos++;
                for (int i = 0; i < numPps && pos + 2 <= payload.Length; i++)
                {
                    int ppsLen = (payload[pos] << 8) | payload[pos + 1];
                    pos += 2;
                    if (pos + ppsLen > payload.Length) break;
                    pps = new byte[ppsLen];
                    Array.Copy(payload, pos, pps, 0, ppsLen);
                    pos += ppsLen;
                }
            }

            if (sps != null && pps != null)
            {
                _activeBuffer.SetSequenceHeader(sps, pps);
                byte profile = (offset + 1 < payload.Length) ? payload[offset + 1] : (byte)0;
                byte level = (offset + 3 < payload.Length) ? payload[offset + 3] : (byte)0;
                Toolbox.Log.Trace("RtmpClient: {0} SPS({1}b) PPS({2}b) naluLenSize={3} profile={4} level={5}",
                    _remoteEndPoint, sps.Length, pps.Length, _naluLengthSize, profile, level);
            }
        }

        private static readonly byte[] AnnexBStartCode = { 0x00, 0x00, 0x00, 0x01 };

        private int _annexBLogCount;
        private int _seiDropLogCount;

        private byte[] ConvertToAnnexB(byte[] payload, int offset, int length, bool isKeyFrame)
        {
            bool logThis = _annexBLogCount < 5;
            _annexBLogCount++;

            using (var ms = new MemoryStream(length + 128))
            {
                // Prepend SPS/PPS on keyframes
                if (isKeyFrame)
                {
                    _activeBuffer.GetSequenceHeader(out byte[] sps, out byte[] pps);
                    if (sps != null)
                    {
                        ms.Write(AnnexBStartCode, 0, 4);
                        ms.Write(sps, 0, sps.Length);
                    }
                    if (pps != null)
                    {
                        ms.Write(AnnexBStartCode, 0, 4);
                        ms.Write(pps, 0, pps.Length);
                    }
                }

                // Convert length-prefixed NALUs to Annex B
                int end = offset + length;
                int naluCount = 0;
                bool hasSliceData = false;
                var naluInfo = logThis ? new System.Text.StringBuilder() : null;

                while (offset + _naluLengthSize <= end)
                {
                    int naluLen = 0;
                    for (int i = 0; i < _naluLengthSize; i++)
                    {
                        naluLen = (naluLen << 8) | payload[offset + i];
                    }
                    offset += _naluLengthSize;

                    if (naluLen <= 0 || naluLen > end - offset)
                    {
                        if (logThis)
                            naluInfo.AppendFormat(" BREAK(len={0},remain={1})", naluLen, end - offset);
                        break;
                    }

                    int nalType = payload[offset] & 0x1F;
                    if (logThis)
                    {
                        naluInfo.AppendFormat(" NAL({0},type={1},{2}b)", naluCount, nalType, naluLen);
                    }

                    // Track whether this message contains actual video slice data (VCL NALUs)
                    // Types 1-5 are VCL: non-IDR slice, partition A/B/C, IDR slice
                    if (nalType >= 1 && nalType <= 5)
                        hasSliceData = true;

                    ms.Write(AnnexBStartCode, 0, 4);
                    ms.Write(payload, offset, naluLen);
                    offset += naluLen;
                    naluCount++;
                }

                // Drop messages that contain only non-VCL NALUs (e.g. SEI-only from DJI drones).
                // These confuse decoders when delivered as standalone frames.
                if (!hasSliceData && !isKeyFrame)
                {
                    if (logThis || _seiDropLogCount < 3)
                    {
                        Toolbox.Log.Trace("RtmpClient: {0} Dropped non-VCL-only message nalus={1}{2}",
                            _remoteEndPoint, naluCount, naluInfo);
                        _seiDropLogCount++;
                    }
                    return null;
                }

                byte[] result = ms.ToArray();
                if (logThis)
                {
                    Toolbox.Log.Trace("RtmpClient: {0} AnnexB key={1} in={2}b out={3}b nalus={4}{5}",
                        _remoteEndPoint, isKeyFrame, length, result.Length, naluCount, naluInfo);
                }
                return result;
            }
        }

        #endregion

        #region Protocol Message Sending

        private void SendProtocolMessage(byte typeId, byte[] payload)
        {
            WriteChunkedMessage(2, typeId, 0, payload);
        }

        private void SendAmfResult(double txId, object props, object info)
        {
            SendAmfCommand("_result", txId, props, info, 0);
        }

        private void SendAmfCommand(string command, double txId, object props, object info, uint streamId)
        {
            var buf = new List<byte>(256);
            Amf0Writer.WriteString(buf, command);
            Amf0Writer.WriteNumber(buf, txId);

            if (props is Dictionary<string, object> propsDict)
                Amf0Writer.WriteObject(buf, propsDict);
            else
                Amf0Writer.WriteNull(buf);

            if (info is Dictionary<string, object> infoDict)
                Amf0Writer.WriteObject(buf, infoDict);
            else if (info is double d)
                Amf0Writer.WriteNumber(buf, d);
            else if (info != null)
                Amf0Writer.WriteNull(buf);

            WriteChunkedMessage(3, 20, streamId, buf.ToArray());
        }

        private void WriteChunkedMessage(int csid, byte typeId, uint streamId, byte[] payload)
        {
            // Type 0 header
            var header = new List<byte>(16);

            // Basic header
            if (csid < 64)
            {
                header.Add((byte)((0 << 6) | csid)); // fmt=0
            }
            else if (csid < 320)
            {
                header.Add((byte)(0 << 6)); // fmt=0, csid=0
                header.Add((byte)(csid - 64));
            }
            else
            {
                header.Add((byte)((0 << 6) | 1)); // fmt=0, csid=1
                int adjusted = csid - 64;
                header.Add((byte)(adjusted & 0xFF));
                header.Add((byte)((adjusted >> 8) & 0xFF));
            }

            // Timestamp (3 bytes)
            header.Add(0); header.Add(0); header.Add(0);
            // Message length (3 bytes, big-endian)
            header.Add((byte)((payload.Length >> 16) & 0xFF));
            header.Add((byte)((payload.Length >> 8) & 0xFF));
            header.Add((byte)(payload.Length & 0xFF));
            // Message type
            header.Add(typeId);
            // Message stream ID (4 bytes, little-endian)
            header.Add((byte)(streamId & 0xFF));
            header.Add((byte)((streamId >> 8) & 0xFF));
            header.Add((byte)((streamId >> 16) & 0xFF));
            header.Add((byte)((streamId >> 24) & 0xFF));

            _stream.Write(header.ToArray(), 0, header.Count);

            // Write payload in chunks
            int offset = 0;
            int remaining = payload.Length;
            bool firstChunk = true;

            while (remaining > 0)
            {
                if (!firstChunk)
                {
                    // Type 3 continuation header
                    if (csid < 64)
                        _stream.WriteByte((byte)((3 << 6) | csid));
                    else if (csid < 320)
                    {
                        _stream.WriteByte((byte)(3 << 6));
                        _stream.WriteByte((byte)(csid - 64));
                    }
                    else
                    {
                        _stream.WriteByte((byte)((3 << 6) | 1));
                        int adjusted = csid - 64;
                        _stream.WriteByte((byte)(adjusted & 0xFF));
                        _stream.WriteByte((byte)((adjusted >> 8) & 0xFF));
                    }
                }

                int toWrite = Math.Min(remaining, _outChunkSize);
                _stream.Write(payload, offset, toWrite);
                offset += toWrite;
                remaining -= toWrite;
                firstChunk = false;
            }

            _stream.Flush();
        }

        #endregion

        #region IO Helpers

        /// <summary>
        /// Strip control characters from attacker-controlled strings to prevent log injection.
        /// </summary>
        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            foreach (char c in input)
            {
                if (char.IsControl(c))
                {
                    var sb = new StringBuilder(input.Length);
                    foreach (char ch in input)
                        sb.Append(char.IsControl(ch) ? '_' : ch);
                    return sb.ToString();
                }
            }
            return input;
        }

        private byte ReadByte()
        {
            int b = _stream.ReadByte();
            if (b < 0) throw new IOException("Connection closed");
            return (byte)b;
        }

        private byte[] ReadBytes(int count)
        {
            var buf = new byte[count];
            ReadBytesInto(buf, 0, count);
            return buf;
        }

        private void ReadBytesInto(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _stream.Read(buffer, offset + total, count - total);
                if (read <= 0) throw new IOException("Connection closed");
                total += read;
            }
        }

        private uint ReadUInt24()
        {
            byte b0 = ReadByte();
            byte b1 = ReadByte();
            byte b2 = ReadByte();
            return (uint)((b0 << 16) | (b1 << 8) | b2);
        }

        private uint ReadUInt32BE()
        {
            byte b0 = ReadByte();
            byte b1 = ReadByte();
            byte b2 = ReadByte();
            byte b3 = ReadByte();
            return (uint)((b0 << 24) | (b1 << 16) | (b2 << 8) | b3);
        }

        private uint ReadUInt32LE()
        {
            byte b0 = ReadByte();
            byte b1 = ReadByte();
            byte b2 = ReadByte();
            byte b3 = ReadByte();
            return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        private static uint ReadUInt32BE(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                          (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static byte[] WriteUInt32BE(uint value)
        {
            return new byte[]
            {
                (byte)((value >> 24) & 0xFF),
                (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)(value & 0xFF)
            };
        }

        private static void WriteUInt32BE(byte[] buf, int offset, uint value)
        {
            buf[offset] = (byte)((value >> 24) & 0xFF);
            buf[offset + 1] = (byte)((value >> 16) & 0xFF);
            buf[offset + 2] = (byte)((value >> 8) & 0xFF);
            buf[offset + 3] = (byte)(value & 0xFF);
        }

        #endregion

        #region Internal Types

        private class ChunkStreamContext
        {
            public int MessageLength;
            public byte MessageTypeId;
            public uint MessageStreamId;
            public uint Timestamp;
            public uint TimestampDelta;
            public byte[] MessageBuffer;
            public int BytesRead;
        }

        #endregion
    }

    internal class RtmpMessage
    {
        public byte TypeId;
        public uint StreamId;
        public uint Timestamp;
        public byte[] Data;
    }
}
