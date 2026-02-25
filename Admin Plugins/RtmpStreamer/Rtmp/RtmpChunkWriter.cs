using System;
using System.IO;

namespace RtmpStreamer.Rtmp
{
    /// <summary>
    /// Writes RTMP chunked messages to a stream.
    /// Handles chunk splitting with fmt 0 (full header) for first chunk
    /// and fmt 3 (continuation) for subsequent chunks.
    /// </summary>
    internal class RtmpChunkWriter
    {
        private readonly Stream _stream;
        private int _chunkSize = 128;

        public RtmpChunkWriter(Stream stream)
        {
            _stream = stream;
        }

        public int ChunkSize
        {
            get => _chunkSize;
            set => _chunkSize = value;
        }

        /// <summary>
        /// Write a complete RTMP message with fmt 0 header (absolute timestamp).
        /// </summary>
        public void WriteMessage(int csid, byte typeId, uint streamId, uint timestamp, byte[] payload)
        {
            bool useExtendedTimestamp = timestamp >= 0xFFFFFF;
            uint headerTimestamp = useExtendedTimestamp ? 0xFFFFFF : timestamp;

            // Fmt 0 basic header
            WriteBasicHeader(0, csid);

            // Message header (11 bytes for fmt 0)
            WriteUInt24(headerTimestamp);                    // timestamp
            WriteUInt24((uint)payload.Length);               // message length
            _stream.WriteByte(typeId);                      // message type ID
            WriteUInt32LE(streamId);                         // message stream ID

            if (useExtendedTimestamp)
                WriteUInt32BE(timestamp);

            // Write payload in chunks
            int offset = 0;
            int remaining = payload.Length;
            bool firstChunk = true;

            while (remaining > 0)
            {
                if (!firstChunk)
                {
                    // Fmt 3 continuation header
                    WriteBasicHeader(3, csid);
                    if (useExtendedTimestamp)
                        WriteUInt32BE(timestamp);
                }

                int toWrite = Math.Min(remaining, _chunkSize);
                _stream.Write(payload, offset, toWrite);
                offset += toWrite;
                remaining -= toWrite;
                firstChunk = false;
            }
        }

        /// <summary>
        /// Write a complete RTMP message with fmt 1 header (relative timestamp delta).
        /// Used for subsequent messages on the same chunk stream where only timestamp delta,
        /// length, and type may differ from the previous message.
        /// </summary>
        public void WriteMessageDelta(int csid, byte typeId, uint timestampDelta, byte[] payload)
        {
            bool useExtendedTimestamp = timestampDelta >= 0xFFFFFF;
            uint headerTimestamp = useExtendedTimestamp ? 0xFFFFFF : timestampDelta;

            // Fmt 1 basic header
            WriteBasicHeader(1, csid);

            // Message header (7 bytes for fmt 1)
            WriteUInt24(headerTimestamp);                    // timestamp delta
            WriteUInt24((uint)payload.Length);               // message length
            _stream.WriteByte(typeId);                      // message type ID

            if (useExtendedTimestamp)
                WriteUInt32BE(timestampDelta);

            // Write payload in chunks
            int offset = 0;
            int remaining = payload.Length;
            bool firstChunk = true;

            while (remaining > 0)
            {
                if (!firstChunk)
                {
                    WriteBasicHeader(3, csid);
                    if (useExtendedTimestamp)
                        WriteUInt32BE(timestampDelta);
                }

                int toWrite = Math.Min(remaining, _chunkSize);
                _stream.Write(payload, offset, toWrite);
                offset += toWrite;
                remaining -= toWrite;
                firstChunk = false;
            }
        }

        public void Flush()
        {
            _stream.Flush();
        }

        private void WriteBasicHeader(int fmt, int csid)
        {
            if (csid < 64)
            {
                _stream.WriteByte((byte)((fmt << 6) | csid));
            }
            else if (csid < 320)
            {
                _stream.WriteByte((byte)(fmt << 6));
                _stream.WriteByte((byte)(csid - 64));
            }
            else
            {
                _stream.WriteByte((byte)((fmt << 6) | 1));
                int adjusted = csid - 64;
                _stream.WriteByte((byte)(adjusted & 0xFF));
                _stream.WriteByte((byte)((adjusted >> 8) & 0xFF));
            }
        }

        private void WriteUInt24(uint value)
        {
            _stream.WriteByte((byte)((value >> 16) & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
            _stream.WriteByte((byte)(value & 0xFF));
        }

        private void WriteUInt32BE(uint value)
        {
            _stream.WriteByte((byte)((value >> 24) & 0xFF));
            _stream.WriteByte((byte)((value >> 16) & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
            _stream.WriteByte((byte)(value & 0xFF));
        }

        private void WriteUInt32LE(uint value)
        {
            _stream.WriteByte((byte)(value & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
            _stream.WriteByte((byte)((value >> 16) & 0xFF));
            _stream.WriteByte((byte)((value >> 24) & 0xFF));
        }
    }
}
