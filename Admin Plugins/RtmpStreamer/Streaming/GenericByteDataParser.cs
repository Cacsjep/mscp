using System;

namespace RtmpStreamer.Streaming
{
    /// <summary>
    /// Parses Milestone GenericByteData video stream packets.
    ///
    /// Header format (32 bytes, big-endian):
    ///   Offset 0-1:   Data type (0x0010 = video stream packet)
    ///   Offset 2-5:   Total length including header
    ///   Offset 6-7:   Codec type (0x000A = H.264, 0x000E = H.265)
    ///   Offset 8-9:   Sequence number
    ///   Offset 10-11: Flags (bit 0 = SYNC/IDR frame)
    ///   Offset 12-19: Timestamp of nearest preceding SYNC frame (UTC ms since epoch)
    ///   Offset 20-27: Timestamp of this picture (UTC ms since epoch)
    ///   Offset 28-31: Reserved
    ///
    /// After the 32-byte header: raw codec data in Annex B format (for H.264/H.265).
    /// </summary>
    internal static class GenericByteDataParser
    {
        public const ushort DataTypeVideoStream = 0x0010;
        public const ushort CodecH264 = 0x000A;
        public const ushort CodecH265 = 0x000E;
        public const ushort CodecJpeg = 0x0001;
        public const int HeaderSize = 32;

        public struct VideoFrameInfo
        {
            public ushort DataType;
            public uint TotalLength;
            public ushort CodecType;
            public ushort SequenceNumber;
            public bool IsKeyFrame;
            public DateTime SyncTimestamp;
            public DateTime PictureTimestamp;
            public byte[] PayloadData;   // Raw H.264 Annex B data
        }

        /// <summary>
        /// Parse a GenericByteData packet and extract the video frame info.
        /// Returns false if the packet is not a valid video stream packet.
        /// </summary>
        public static bool TryParse(byte[] data, out VideoFrameInfo frame)
        {
            frame = default;

            if (data == null || data.Length < HeaderSize)
                return false;

            ushort dataType = (ushort)((data[0] << 8) | data[1]);
            if (dataType != DataTypeVideoStream)
                return false;

            uint totalLength = (uint)((data[2] << 24) | (data[3] << 16) | (data[4] << 8) | data[5]);
            ushort codecType = (ushort)((data[6] << 8) | data[7]);
            ushort seqNum = (ushort)((data[8] << 8) | data[9]);
            ushort flags = (ushort)((data[10] << 8) | data[11]);
            bool isSync = (flags & 0x0001) != 0;

            long syncMs = ReadInt64BE(data, 12);
            long pictureMs = ReadInt64BE(data, 20);

            int payloadLength = data.Length - HeaderSize;
            if (payloadLength <= 0)
                return false;

            byte[] payload = new byte[payloadLength];
            Buffer.BlockCopy(data, HeaderSize, payload, 0, payloadLength);

            frame = new VideoFrameInfo
            {
                DataType = dataType,
                TotalLength = totalLength,
                CodecType = codecType,
                SequenceNumber = seqNum,
                IsKeyFrame = isSync,
                SyncTimestamp = MillisecondsToDateTime(syncMs),
                PictureTimestamp = MillisecondsToDateTime(pictureMs),
                PayloadData = payload
            };

            return true;
        }

        private static long ReadInt64BE(byte[] data, int offset)
        {
            long value = 0;
            for (int i = 0; i < 8; i++)
            {
                value = (value << 8) | data[offset + i];
            }
            return value;
        }

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static DateTime MillisecondsToDateTime(long ms)
        {
            if (ms <= 0)
                return DateTime.MinValue;
            try
            {
                return UnixEpoch.AddMilliseconds(ms);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}
