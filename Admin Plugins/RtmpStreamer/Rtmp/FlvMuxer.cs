using System;
using System.Collections.Generic;
using System.IO;

namespace RTMPStreamer.Rtmp
{
    /// <summary>
    /// Converts H.264 Annex B data into FLV video tag payloads for RTMP streaming.
    /// Handles:
    /// - Annex B → length-prefixed NALU conversion
    /// - SPS/PPS extraction and AVCDecoderConfigurationRecord creation
    /// - AVC sequence header generation
    /// - FLV video tag formatting
    /// </summary>
    internal class FlvMuxer
    {
        private byte[] _sps;
        private byte[] _pps;
        private bool _sequenceHeaderSent;

        /// <summary>
        /// Whether the AVC sequence header has been sent (SPS/PPS received and transmitted).
        /// </summary>
        public bool IsReady => _sequenceHeaderSent;

        /// <summary>
        /// Build the AVC sequence header FLV tag payload from the current SPS/PPS.
        /// This must be sent before any video frames.
        /// Returns null if SPS/PPS not yet available.
        /// </summary>
        public byte[] BuildSequenceHeader()
        {
            if (_sps == null || _pps == null)
                return null;

            // Build AVCDecoderConfigurationRecord
            var record = new List<byte>();
            record.Add(1);                          // configurationVersion
            record.Add(_sps.Length > 1 ? _sps[1] : (byte)0); // AVCProfileIndication
            record.Add(_sps.Length > 2 ? _sps[2] : (byte)0); // profile_compatibility
            record.Add(_sps.Length > 3 ? _sps[3] : (byte)0); // AVCLevelIndication
            record.Add(0xFF);                       // lengthSizeMinusOne = 3 (4-byte NALU lengths) | reserved 0xFC
            record.Add((byte)(0xE0 | 1));           // numOfSPS = 1 | reserved 0xE0
            record.Add((byte)(_sps.Length >> 8));    // SPS length (big-endian)
            record.Add((byte)(_sps.Length & 0xFF));
            record.AddRange(_sps);                  // SPS data
            record.Add(1);                          // numOfPPS
            record.Add((byte)(_pps.Length >> 8));    // PPS length (big-endian)
            record.Add((byte)(_pps.Length & 0xFF));
            record.AddRange(_pps);                  // PPS data

            // Wrap in FLV video tag payload
            var tag = new List<byte>();
            tag.Add(0x17);  // FrameType=1 (keyframe) | CodecID=7 (AVC)
            tag.Add(0x00);  // AVC packet type = 0 (sequence header)
            tag.Add(0x00);  // Composition time offset (3 bytes)
            tag.Add(0x00);
            tag.Add(0x00);
            tag.AddRange(record);

            _sequenceHeaderSent = true;
            return tag.ToArray();
        }

        /// <summary>
        /// Convert an H.264 Annex B frame to an FLV video tag payload.
        /// Automatically extracts SPS/PPS from keyframes.
        /// Returns the FLV video tag payload, or null if not ready.
        /// Also returns the sequence header separately if SPS/PPS was just discovered.
        /// </summary>
        public byte[] MuxFrame(byte[] annexBData, bool isKeyFrame, out byte[] newSequenceHeader)
        {
            newSequenceHeader = null;

            // Parse Annex B NALUs
            var nalus = ParseAnnexBNalus(annexBData);
            if (nalus.Count == 0)
                return null;

            // Extract SPS/PPS from keyframes
            if (isKeyFrame)
            {
                bool spsChanged = false;
                foreach (var nalu in nalus)
                {
                    int naluType = nalu[0] & 0x1F;
                    if (naluType == 7) // SPS
                    {
                        if (!ByteArrayEquals(_sps, nalu))
                        {
                            _sps = nalu;
                            spsChanged = true;
                        }
                    }
                    else if (naluType == 8) // PPS
                    {
                        if (!ByteArrayEquals(_pps, nalu))
                        {
                            _pps = nalu;
                            spsChanged = true;
                        }
                    }
                }

                if (spsChanged || !_sequenceHeaderSent)
                {
                    newSequenceHeader = BuildSequenceHeader();
                }
            }

            if (!_sequenceHeaderSent)
                return null; // Can't send frames until we have SPS/PPS

            // Build FLV video tag payload with length-prefixed NALUs
            // Skip SPS (type 7), PPS (type 8), and AUD (type 9) NALUs - they're in the sequence header
            var videoNalus = new List<byte[]>();
            foreach (var nalu in nalus)
            {
                int naluType = nalu[0] & 0x1F;
                if (naluType != 7 && naluType != 8 && naluType != 9)
                    videoNalus.Add(nalu);
            }

            if (videoNalus.Count == 0)
                return null;

            // Calculate total size
            int dataSize = 0;
            foreach (var nalu in videoNalus)
                dataSize += 4 + nalu.Length; // 4-byte length prefix + NALU data

            var tag = new List<byte>(5 + dataSize);
            tag.Add(isKeyFrame ? (byte)0x17 : (byte)0x27); // FrameType | CodecID=7 (AVC)
            tag.Add(0x01);  // AVC packet type = 1 (NALU)
            tag.Add(0x00);  // Composition time offset (3 bytes) - 0 for live
            tag.Add(0x00);
            tag.Add(0x00);

            // Write length-prefixed NALUs
            foreach (var nalu in videoNalus)
            {
                // 4-byte big-endian NALU length
                tag.Add((byte)((nalu.Length >> 24) & 0xFF));
                tag.Add((byte)((nalu.Length >> 16) & 0xFF));
                tag.Add((byte)((nalu.Length >> 8) & 0xFF));
                tag.Add((byte)(nalu.Length & 0xFF));
                tag.AddRange(nalu);
            }

            return tag.ToArray();
        }

        /// <summary>
        /// Build the AAC audio sequence header FLV tag payload.
        /// Must be sent before any audio data frames.
        /// FLV AudioTagHeader: SoundFormat=10(AAC), Rate=3(44kHz), Size=1(16bit), Type=1(stereo) = 0xAF
        /// </summary>
        public byte[] BuildAudioSequenceHeader()
        {
            var asc = SilentAacGenerator.AudioSpecificConfig;
            var tag = new byte[2 + asc.Length];
            tag[0] = 0xAF; // AAC, 44kHz, 16-bit, stereo
            tag[1] = 0x00; // AACPacketType = 0 (sequence header)
            Buffer.BlockCopy(asc, 0, tag, 2, asc.Length);
            return tag;
        }

        /// <summary>
        /// Build a silent AAC audio frame as FLV tag payload.
        /// Returns the same bytes every call (can be cached by caller).
        /// </summary>
        public byte[] BuildSilentAudioFrame()
        {
            var frame = SilentAacGenerator.SilentFrame;
            var tag = new byte[2 + frame.Length];
            tag[0] = 0xAF; // AAC, 44kHz, 16-bit, stereo
            tag[1] = 0x01; // AACPacketType = 1 (raw)
            Buffer.BlockCopy(frame, 0, tag, 2, frame.Length);
            return tag;
        }

        /// <summary>
        /// Reset state (e.g., on reconnect).
        /// </summary>
        public void Reset()
        {
            _sps = null;
            _pps = null;
            _sequenceHeaderSent = false;
        }

        /// <summary>
        /// Parse Annex B byte stream into individual NALUs (without start codes).
        /// </summary>
        private static List<byte[]> ParseAnnexBNalus(byte[] data)
        {
            var nalus = new List<byte[]>();
            int i = 0;
            int len = data.Length;

            while (i < len)
            {
                // Find start code: 0x000001 or 0x00000001
                int startCodeLen = 0;
                if (i + 2 < len && data[i] == 0 && data[i + 1] == 0)
                {
                    if (i + 3 < len && data[i + 2] == 0 && data[i + 3] == 1)
                        startCodeLen = 4;
                    else if (data[i + 2] == 1)
                        startCodeLen = 3;
                }

                if (startCodeLen == 0)
                {
                    i++;
                    continue;
                }

                int naluStart = i + startCodeLen;

                // Find the next start code or end of data
                int naluEnd = len;
                for (int j = naluStart + 1; j < len - 2; j++)
                {
                    if (data[j] == 0 && data[j + 1] == 0 &&
                        (data[j + 2] == 1 || (j + 3 < len && data[j + 2] == 0 && data[j + 3] == 1)))
                    {
                        naluEnd = j;
                        break;
                    }
                }

                // Remove trailing zeros from NALU
                while (naluEnd > naluStart && data[naluEnd - 1] == 0)
                    naluEnd--;

                if (naluEnd > naluStart)
                {
                    var nalu = new byte[naluEnd - naluStart];
                    Buffer.BlockCopy(data, naluStart, nalu, 0, nalu.Length);
                    nalus.Add(nalu);
                }

                i = naluEnd;
            }

            return nalus;
        }

        private static bool ByteArrayEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
