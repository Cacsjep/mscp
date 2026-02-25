using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace RTMPStreamer.Rtmp
{
    /// <summary>
    /// RTMP client that connects to an RTMP server (YouTube, Twitch, etc.)
    /// and publishes H.264 video data as a live stream.
    ///
    /// Usage:
    ///   1. Call Connect() with the full RTMP URL (e.g., rtmp://a.rtmp.youtube.com/live2/stream-key)
    ///   2. Call SendVideoData() for each H.264 frame (as FLV video tag payload)
    ///   3. Call Disconnect() when done
    /// </summary>
    internal class RtmpPublisher : IDisposable
    {
        private const int HandshakeSize = 1536;
        private const int DefaultChunkSize = 128;
        private const int OutputChunkSize = 4096;

        private TcpClient _tcpClient;
        private NetworkStream _netStream;
        private SslStream _sslStream;
        private Stream _readStream;  // points to _netStream or _sslStream for all reads
        private BufferedStream _bufferedStream;
        private RtmpChunkWriter _chunkWriter;
        private readonly object _writeLock = new object();

        private string _host;
        private int _port;
        private string _app;
        private string _streamKey;
        private bool _useTls;

        private bool _connected;
        private bool _publishing;
        private uint _streamId;

        // Chunk stream IDs for different message types
        private const int CsidProtocol = 2;
        private const int CsidCommand = 3;
        private const int CsidAudio = 4;
        private const int CsidVideo = 6;

        // Track timestamps for delta encoding
        private uint _lastVideoTimestamp;
        private bool _firstVideoMessage = true;
        private uint _lastAudioTimestamp;
        private bool _firstAudioMessage = true;

        public bool IsConnected => _connected;
        public bool IsPublishing => _publishing;

        /// <summary>
        /// Connect to an RTMP/RTMPS server and start publishing.
        /// URL format: rtmp://host:port/app/streamkey or rtmps://host:port/app/streamkey
        /// </summary>
        public void Connect(string rtmpUrl, bool allowUntrustedCerts = false)
        {
            ParseUrl(rtmpUrl);

            PluginLog.Info($"[RTMP] Connecting to {_host}:{_port} (TLS={_useTls})");

            _tcpClient = new TcpClient();
            _tcpClient.NoDelay = true;
            _tcpClient.SendTimeout = 10000;
            _tcpClient.ReceiveTimeout = 10000;
            _tcpClient.SendBufferSize = 256 * 1024;
            _tcpClient.Connect(_host, _port);

            PluginLog.Info($"[RTMP] TCP connected to {_host}:{_port}");

            _netStream = _tcpClient.GetStream();

            if (_useTls)
            {
                PluginLog.Info("[RTMP] Starting TLS handshake");
                _sslStream = new SslStream(_netStream, false, (sender, cert, chain, errors) =>
                {
                    if (allowUntrustedCerts) return true;
                    return errors == SslPolicyErrors.None;
                });
                _sslStream.AuthenticateAsClient(_host);
                PluginLog.Info($"[RTMP] TLS handshake complete (protocol={_sslStream.SslProtocol})");
                _readStream = _sslStream;
                _bufferedStream = new BufferedStream(_sslStream, 64 * 1024);
            }
            else
            {
                _readStream = _netStream;
                _bufferedStream = new BufferedStream(_netStream, 64 * 1024);
            }

            _chunkWriter = new RtmpChunkWriter(_bufferedStream);

            PluginLog.Info("[RTMP] Performing RTMP handshake");
            PerformHandshake();
            _connected = true;
            PluginLog.Info("[RTMP] RTMP handshake complete");

            PluginLog.Info($"[RTMP] Sending connect command (app={_app})");
            SendConnect();
            ReadResponses(); // Read until we get connect result
            PluginLog.Info("[RTMP] Connect accepted by server");

            SendReleaseStream();
            SendFCPublish();
            SendCreateStream();
            ReadResponses(); // Read until we get createStream result
            PluginLog.Info($"[RTMP] Stream created (streamId={_streamId})");

            SendPublish();
            ReadResponses(); // Read until we get onStatus publish start
            PluginLog.Info("[RTMP] Publish started successfully");

            // Set our output chunk size
            SendSetChunkSize(OutputChunkSize);
            _chunkWriter.ChunkSize = OutputChunkSize;

            _publishing = true;
        }

        /// <summary>
        /// Send an audio frame as an FLV audio tag payload (RTMP message type 8).
        /// </summary>
        public void SendAudioData(byte[] flvAudioTagPayload, uint timestampMs)
        {
            if (!_publishing)
                throw new InvalidOperationException("Not publishing");

            lock (_writeLock)
            {
                if (_firstAudioMessage)
                {
                    _chunkWriter.WriteMessage(CsidAudio, 8, _streamId, timestampMs, flvAudioTagPayload);
                    _lastAudioTimestamp = timestampMs;
                    _firstAudioMessage = false;
                }
                else
                {
                    uint delta = timestampMs - _lastAudioTimestamp;
                    _chunkWriter.WriteMessageDelta(CsidAudio, 8, delta, flvAudioTagPayload);
                    _lastAudioTimestamp = timestampMs;
                }
                _chunkWriter.Flush();
            }
        }

        /// <summary>
        /// Send a video frame as an FLV video tag payload.
        /// The payload should be produced by FlvMuxer (includes FLV video tag header bytes).
        /// </summary>
        public void SendVideoData(byte[] flvVideoTagPayload, uint timestampMs)
        {
            if (!_publishing)
                throw new InvalidOperationException("Not publishing");

            lock (_writeLock)
            {
                if (_firstVideoMessage)
                {
                    _chunkWriter.WriteMessage(CsidVideo, 9, _streamId, timestampMs, flvVideoTagPayload);
                    _lastVideoTimestamp = timestampMs;
                    _firstVideoMessage = false;
                }
                else
                {
                    uint delta = timestampMs - _lastVideoTimestamp;
                    _chunkWriter.WriteMessageDelta(CsidVideo, 9, delta, flvVideoTagPayload);
                    _lastVideoTimestamp = timestampMs;
                }
                _chunkWriter.Flush();
            }
        }

        /// <summary>
        /// Disconnect from the RTMP server.
        /// </summary>
        public void Disconnect()
        {
            var wasPublishing = _publishing;
            _publishing = false;
            _connected = false;
            _firstVideoMessage = true;
            _lastVideoTimestamp = 0;
            _firstAudioMessage = true;
            _lastAudioTimestamp = 0;

            if (wasPublishing)
                PluginLog.Info("[RTMP] Disconnecting from server");

            try
            {
                if (_tcpClient?.Connected == true)
                {
                    SendFCUnpublish();
                    SendDeleteStream();
                }
            }
            catch { }

            try { _bufferedStream?.Dispose(); } catch { }
            try { _sslStream?.Dispose(); } catch { }
            try { _netStream?.Dispose(); } catch { }
            try { _tcpClient?.Close(); } catch { }

            _bufferedStream = null;
            _sslStream = null;
            _readStream = null;
            _netStream = null;
            _tcpClient = null;
            _chunkWriter = null;
        }

        public void Dispose()
        {
            Disconnect();
        }

        #region URL Parsing

        private void ParseUrl(string url)
        {
            // Format: rtmp://host:port/app/streamkey  or  rtmps://host:port/app/streamkey
            int defaultPort;
            string remainder;

            if (url.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
            {
                _useTls = true;
                defaultPort = 443;
                remainder = url.Substring(8); // strip rtmps://
            }
            else if (url.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase))
            {
                _useTls = false;
                defaultPort = 1935;
                remainder = url.Substring(7); // strip rtmp://
            }
            else
            {
                throw new ArgumentException("URL must start with rtmp:// or rtmps://");
            }

            // Split host:port from path
            int slashIdx = remainder.IndexOf('/');
            if (slashIdx < 0)
                throw new ArgumentException("URL must contain app name after host");

            string hostPort = remainder.Substring(0, slashIdx);
            string path = remainder.Substring(slashIdx + 1);

            // Parse host and port
            int colonIdx = hostPort.IndexOf(':');
            if (colonIdx >= 0)
            {
                _host = hostPort.Substring(0, colonIdx);
                _port = int.Parse(hostPort.Substring(colonIdx + 1));
            }
            else
            {
                _host = hostPort;
                _port = defaultPort;
            }

            // Split path into app and stream key
            // For YouTube: app=live2, streamkey=xxxx-xxxx-xxxx-xxxx
            // The app is the first path segment, stream key is the rest
            int keySlashIdx = path.IndexOf('/');
            if (keySlashIdx >= 0)
            {
                _app = path.Substring(0, keySlashIdx);
                _streamKey = path.Substring(keySlashIdx + 1);
            }
            else
            {
                _app = path;
                _streamKey = "";
            }
        }

        #endregion

        #region Handshake

        private void PerformHandshake()
        {
            // Send C0 (version 3) + C1 (1536 bytes: timestamp + zeros + random)
            _readStream.WriteByte(3);

            var c1 = new byte[HandshakeSize];
            var rng = new Random();
            rng.NextBytes(c1);
            // Timestamp = 0
            c1[0] = 0; c1[1] = 0; c1[2] = 0; c1[3] = 0;
            // Zero
            c1[4] = 0; c1[5] = 0; c1[6] = 0; c1[7] = 0;
            _readStream.Write(c1, 0, HandshakeSize);
            _readStream.Flush();

            // Read S0 + S1 + S2
            int s0 = _readStream.ReadByte();
            if (s0 < 0) throw new IOException("Connection closed during handshake");
            if (s0 != 3) throw new InvalidDataException($"Unsupported RTMP version from server: {s0}");

            byte[] s1 = ReadExact(HandshakeSize);
            byte[] s2 = ReadExact(HandshakeSize);

            // Send C2 (echo of S1)
            _readStream.Write(s1, 0, HandshakeSize);
            _readStream.Flush();
        }

        #endregion

        #region RTMP Commands

        private double _txId = 0;

        private double NextTxId()
        {
            return ++_txId;
        }

        private void SendConnect()
        {
            var buf = new List<byte>(256);
            Amf0Writer.WriteString(buf, "connect");
            Amf0Writer.WriteNumber(buf, NextTxId());
            Amf0Writer.WriteObject(buf, new Dictionary<string, object>
            {
                {"app", _app},
                {"type", "nonprivate"},
                {"flashVer", "FMLE/3.0 (compatible; FMSc/1.0)"},
                {"tcUrl", $"{(_useTls ? "rtmps" : "rtmp")}://{_host}:{_port}/{_app}"}
            });

            lock (_writeLock)
            {
                _chunkWriter.WriteMessage(CsidCommand, 20, 0, 0, buf.ToArray());
                _chunkWriter.Flush();
            }
        }

        private void SendReleaseStream()
        {
            var buf = new List<byte>(64);
            Amf0Writer.WriteString(buf, "releaseStream");
            Amf0Writer.WriteNumber(buf, NextTxId());
            Amf0Writer.WriteNull(buf);
            Amf0Writer.WriteString(buf, _streamKey);

            lock (_writeLock)
            {
                _chunkWriter.WriteMessage(CsidCommand, 20, 0, 0, buf.ToArray());
                _chunkWriter.Flush();
            }
        }

        private void SendFCPublish()
        {
            var buf = new List<byte>(64);
            Amf0Writer.WriteString(buf, "FCPublish");
            Amf0Writer.WriteNumber(buf, NextTxId());
            Amf0Writer.WriteNull(buf);
            Amf0Writer.WriteString(buf, _streamKey);

            lock (_writeLock)
            {
                _chunkWriter.WriteMessage(CsidCommand, 20, 0, 0, buf.ToArray());
                _chunkWriter.Flush();
            }
        }

        private void SendCreateStream()
        {
            var buf = new List<byte>(32);
            Amf0Writer.WriteString(buf, "createStream");
            Amf0Writer.WriteNumber(buf, NextTxId());
            Amf0Writer.WriteNull(buf);

            lock (_writeLock)
            {
                _chunkWriter.WriteMessage(CsidCommand, 20, 0, 0, buf.ToArray());
                _chunkWriter.Flush();
            }
        }

        private void SendPublish()
        {
            var buf = new List<byte>(64);
            Amf0Writer.WriteString(buf, "publish");
            Amf0Writer.WriteNumber(buf, NextTxId());
            Amf0Writer.WriteNull(buf);
            Amf0Writer.WriteString(buf, _streamKey);
            Amf0Writer.WriteString(buf, "live");

            lock (_writeLock)
            {
                _chunkWriter.WriteMessage(CsidCommand, 20, _streamId, 0, buf.ToArray());
                _chunkWriter.Flush();
            }
        }

        private void SendFCUnpublish()
        {
            var buf = new List<byte>(64);
            Amf0Writer.WriteString(buf, "FCUnpublish");
            Amf0Writer.WriteNumber(buf, NextTxId());
            Amf0Writer.WriteNull(buf);
            Amf0Writer.WriteString(buf, _streamKey);

            lock (_writeLock)
            {
                _chunkWriter.WriteMessage(CsidCommand, 20, 0, 0, buf.ToArray());
                _chunkWriter.Flush();
            }
        }

        private void SendDeleteStream()
        {
            var buf = new List<byte>(32);
            Amf0Writer.WriteString(buf, "deleteStream");
            Amf0Writer.WriteNumber(buf, NextTxId());
            Amf0Writer.WriteNull(buf);
            Amf0Writer.WriteNumber(buf, _streamId);

            lock (_writeLock)
            {
                _chunkWriter.WriteMessage(CsidCommand, 20, 0, 0, buf.ToArray());
                _chunkWriter.Flush();
            }
        }

        private void SendSetChunkSize(int size)
        {
            var payload = new byte[4];
            payload[0] = (byte)((size >> 24) & 0x7F); // MSB must be 0
            payload[1] = (byte)((size >> 16) & 0xFF);
            payload[2] = (byte)((size >> 8) & 0xFF);
            payload[3] = (byte)(size & 0xFF);

            lock (_writeLock)
            {
                _chunkWriter.WriteMessage(CsidProtocol, 1, 0, 0, payload);
                _chunkWriter.Flush();
            }
        }

        #endregion

        #region Response Reading

        private int _inChunkSize = DefaultChunkSize;
        private readonly Dictionary<int, ChunkStreamContext> _chunkStreams = new Dictionary<int, ChunkStreamContext>();

        /// <summary>
        /// Read and process RTMP messages from the server until we receive
        /// the expected response (connect result, createStream result, or publish status).
        /// </summary>
        private void ReadResponses()
        {
            int maxAttempts = 50; // prevent infinite loops
            for (int i = 0; i < maxAttempts; i++)
            {
                var msg = ReadMessage();
                if (msg == null)
                    throw new IOException("Connection closed while waiting for server response");

                switch (msg.TypeId)
                {
                    case 1: // Set Chunk Size
                        if (msg.Data.Length >= 4)
                            _inChunkSize = (int)ReadUInt32BE(msg.Data, 0) & 0x7FFFFFFF;
                        break;

                    case 2: // Abort
                        break;

                    case 3: // Acknowledgement
                        break;

                    case 4: // User Control Message
                        break;

                    case 5: // Window Acknowledgement Size
                        // Server tells us its window size; we should send ack when we've received that many bytes
                        // For publishing, we can send a Window Ack Size back
                        if (msg.Data.Length >= 4)
                        {
                            uint windowSize = ReadUInt32BE(msg.Data, 0);
                            SendWindowAckSize(windowSize);
                        }
                        break;

                    case 6: // Set Peer Bandwidth
                        break;

                    case 20: // AMF0 Command
                        if (HandleServerCommand(msg.Data))
                            return; // Got the response we were waiting for
                        break;
                }
            }
        }

        private bool HandleServerCommand(byte[] data)
        {
            var args = Amf0Reader.ParseCommand(data);
            if (args.Count < 2) return false;

            string command = args[0] as string;

            switch (command)
            {
                case "_result":
                    double txId = args.Count > 1 && args[1] is double d ? d : 0;
                    // Check if this is a createStream result (returns stream ID)
                    if (args.Count > 3 && args[3] is double sid)
                    {
                        _streamId = (uint)sid;
                    }
                    return true;

                case "_error":
                    string errorDesc = "Unknown error";
                    if (args.Count > 3 && args[3] is Dictionary<string, object> errInfo)
                    {
                        if (errInfo.TryGetValue("description", out object desc))
                            errorDesc = desc?.ToString();
                    }
                    throw new Exception($"RTMP error: {errorDesc}");

                case "onStatus":
                    if (args.Count > 3 && args[3] is Dictionary<string, object> statusInfo)
                    {
                        if (statusInfo.TryGetValue("code", out object code))
                        {
                            string codeStr = code?.ToString() ?? "";
                            if (codeStr.Contains("Publish.Start"))
                                return true;
                            if (codeStr.Contains("Error") || codeStr.Contains("Failed") || codeStr.Contains("BadName"))
                                throw new Exception($"RTMP publish failed: {codeStr}");
                        }
                    }
                    return true;

                case "onBWDone":
                    return false; // Informational, keep reading
            }

            return false;
        }

        private void SendWindowAckSize(uint size)
        {
            var payload = new byte[4];
            payload[0] = (byte)((size >> 24) & 0xFF);
            payload[1] = (byte)((size >> 16) & 0xFF);
            payload[2] = (byte)((size >> 8) & 0xFF);
            payload[3] = (byte)(size & 0xFF);

            lock (_writeLock)
            {
                _chunkWriter.WriteMessage(CsidProtocol, 5, 0, 0, payload);
                _chunkWriter.Flush();
            }
        }

        private RtmpMessage ReadMessage()
        {
            while (true)
            {
                int firstByte = _readStream.ReadByte();
                if (firstByte < 0) return null;

                int fmt = (firstByte >> 6) & 0x03;
                int csid = firstByte & 0x3F;

                if (csid == 0)
                    csid = ReadOneByte() + 64;
                else if (csid == 1)
                {
                    int b0 = ReadOneByte();
                    int b1 = ReadOneByte();
                    csid = b1 * 256 + b0 + 64;
                }

                if (!_chunkStreams.TryGetValue(csid, out var ctx))
                {
                    ctx = new ChunkStreamContext();
                    _chunkStreams[csid] = ctx;
                }

                uint timestamp = 0;
                bool hasExtendedTimestamp = false;

                if (fmt == 0)
                {
                    timestamp = ReadUInt24FromStream();
                    ctx.MessageLength = (int)ReadUInt24FromStream();
                    ctx.MessageTypeId = (byte)ReadOneByte();
                    ctx.MessageStreamId = ReadUInt32LEFromStream();
                    hasExtendedTimestamp = timestamp == 0xFFFFFF;
                    if (!hasExtendedTimestamp)
                        ctx.Timestamp = timestamp;
                }
                else if (fmt == 1)
                {
                    timestamp = ReadUInt24FromStream();
                    ctx.MessageLength = (int)ReadUInt24FromStream();
                    ctx.MessageTypeId = (byte)ReadOneByte();
                    hasExtendedTimestamp = timestamp == 0xFFFFFF;
                    if (!hasExtendedTimestamp)
                        ctx.TimestampDelta = timestamp;
                }
                else if (fmt == 2)
                {
                    timestamp = ReadUInt24FromStream();
                    hasExtendedTimestamp = timestamp == 0xFFFFFF;
                    if (!hasExtendedTimestamp)
                        ctx.TimestampDelta = timestamp;
                }

                if (hasExtendedTimestamp)
                {
                    uint extTs = ReadUInt32BEFromStream();
                    if (fmt == 0)
                        ctx.Timestamp = extTs;
                    else
                        ctx.TimestampDelta = extTs;
                }

                if (fmt != 0)
                    ctx.Timestamp += ctx.TimestampDelta;

                if (ctx.BytesRead == 0)
                    ctx.MessageBuffer = new byte[ctx.MessageLength];

                int remaining = ctx.MessageLength - ctx.BytesRead;
                int toRead = Math.Min(remaining, _inChunkSize);
                ReadExactInto(ctx.MessageBuffer, ctx.BytesRead, toRead);
                ctx.BytesRead += toRead;

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
        }

        #endregion

        #region IO Helpers

        private byte[] ReadExact(int count)
        {
            var buf = new byte[count];
            ReadExactInto(buf, 0, count);
            return buf;
        }

        private void ReadExactInto(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _readStream.Read(buffer, offset + total, count - total);
                if (read <= 0) throw new IOException("Connection closed");
                total += read;
            }
        }

        private int ReadOneByte()
        {
            int b = _readStream.ReadByte();
            if (b < 0) throw new IOException("Connection closed");
            return b;
        }

        private uint ReadUInt24FromStream()
        {
            int b0 = ReadOneByte();
            int b1 = ReadOneByte();
            int b2 = ReadOneByte();
            return (uint)((b0 << 16) | (b1 << 8) | b2);
        }

        private uint ReadUInt32BEFromStream()
        {
            int b0 = ReadOneByte();
            int b1 = ReadOneByte();
            int b2 = ReadOneByte();
            int b3 = ReadOneByte();
            return (uint)((b0 << 24) | (b1 << 16) | (b2 << 8) | b3);
        }

        private uint ReadUInt32LEFromStream()
        {
            int b0 = ReadOneByte();
            int b1 = ReadOneByte();
            int b2 = ReadOneByte();
            int b3 = ReadOneByte();
            return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        private static uint ReadUInt32BE(byte[] data, int offset)
        {
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                          (data[offset + 2] << 8) | data[offset + 3]);
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

        private class RtmpMessage
        {
            public byte TypeId;
            public uint StreamId;
            public uint Timestamp;
            public byte[] Data;
        }

        #endregion
    }
}
