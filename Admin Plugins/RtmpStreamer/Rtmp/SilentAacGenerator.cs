namespace RTMPStreamer.Rtmp
{
    /// <summary>
    /// Provides pre-computed constants for generating a silent AAC-LC audio track.
    /// Used to satisfy the audio requirement of platforms like YouTube and Twitch.
    ///
    /// AudioSpecificConfig: AAC-LC, 44100 Hz, stereo
    ///   audioObjectType=2 (AAC-LC), samplingFrequencyIndex=4 (44100), channelConfiguration=2 (stereo)
    ///
    /// SilentFrame: Raw AAC frame (no ADTS header) encoding 1024 samples of silence.
    ///   Contains a Channel Pair Element (CPE) with max_sfb=0 (no spectral bands = silence),
    ///   followed by an END element, byte-aligned.
    /// </summary>
    internal static class SilentAacGenerator
    {
        /// <summary>
        /// AudioSpecificConfig for AAC-LC 44100 Hz stereo (2 bytes).
        /// Bits: 00010 0100 0010 000 = audioObjectType=2, samplingFreqIdx=4, channelConfig=2
        /// </summary>
        public static readonly byte[] AudioSpecificConfig = { 0x12, 0x10 };

        /// <summary>
        /// Raw silent AAC-LC frame for 44100 Hz stereo (6 bytes, no ADTS header).
        ///
        /// Bitstream layout:
        ///   CPE id=001, tag=0000, common_window=1
        ///   ics_info: reserved=0, window_seq=00(ONLY_LONG), shape=1(KBD), max_sfb=000000, predictor=0
        ///   ms_mask_present=00
        ///   Left  ICS: global_gain=00000000, pulse=0, tns=0, gain_control=0
        ///   Right ICS: global_gain=00000000, pulse=0, tns=0, gain_control=0
        ///   END element=111, padding=00
        ///
        /// Bytes: 00100001 00010000 00000000 00000000 00000000 00011100
        /// </summary>
        public static readonly byte[] SilentFrame = { 0x21, 0x10, 0x00, 0x00, 0x00, 0x1C };

        public const int SampleRate = 44100;
        public const int SamplesPerFrame = 1024;

        /// <summary>Duration of one AAC frame in milliseconds (~23.22 ms).</summary>
        public const double FrameDurationMs = (double)SamplesPerFrame / SampleRate * 1000.0;
    }
}
