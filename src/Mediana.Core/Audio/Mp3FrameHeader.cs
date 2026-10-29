// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Core.Audio
{
    /// <summary>MPEG Layer III frame header. Free-format framing is not supported.
    /// Frame duration includes coded delay/padding; a header does not establish independent decodability.</summary>
    public class Mp3FrameHeader
    {
        public const string CodecId = "mp3";
        public const int HeaderBytes = 4;

        private static ReadOnlySpan<int> Mpeg1BitRates => [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
        private static ReadOnlySpan<int> LowRateBitRates => [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];

        private Mp3FrameHeader(uint word)
        {
            if ((word & 0xffe00000) != 0xffe00000)
            {
                throw new InvalidDataException("The MPEG audio sync bits are invalid.");
            }
            Version = ((word >> 19) & 3) switch
            {
                0 => MpegAudioVersion.Mpeg25,
                1 => throw new InvalidDataException("The MPEG audio version is reserved."),
                2 => MpegAudioVersion.Mpeg2,
                3 => MpegAudioVersion.Mpeg1,
                _ => throw new InvalidOperationException()
            };
            switch ((word >> 17) & 3)
            {
                case 0:
                    throw new InvalidDataException("The MPEG audio layer is reserved.");
                case 1:
                    break;
                case 2:
                case 3:
                    throw new NotSupportedException("Only MPEG audio Layer III framing is supported.");
                default:
                    throw new InvalidOperationException();
            }
            int bitRateIndex = (int)((word >> 12) & 15);
            if (bitRateIndex == 15 || ((word >> 10) & 3) == 3 || (word & 3) == 2)
            {
                throw new InvalidDataException("The MP3 bit rate, sample rate or emphasis is reserved.");
            }
            if (bitRateIndex == 0)
            {
                throw new NotSupportedException("Free-format MP3 requires explicit frame-size discovery and is not supported.");
            }
            BitRate = Version switch
            {
                MpegAudioVersion.Mpeg1 => Mpeg1BitRates[bitRateIndex] * 1000,
                MpegAudioVersion.Mpeg2 or MpegAudioVersion.Mpeg25 => LowRateBitRates[bitRateIndex] * 1000,
                _ => throw new InvalidOperationException()
            };
            int baseRate = ((word >> 10) & 3) switch
            {
                0 => 44100,
                1 => 48000,
                2 => 32000,
                _ => throw new InvalidOperationException()
            };
            SampleRate = Version switch
            {
                MpegAudioVersion.Mpeg1 => baseRate,
                MpegAudioVersion.Mpeg2 => baseRate / 2,
                MpegAudioVersion.Mpeg25 => baseRate / 4,
                _ => throw new InvalidOperationException()
            };
            SamplesPerFrame = Version switch
            {
                MpegAudioVersion.Mpeg1 => 1152,
                MpegAudioVersion.Mpeg2 or MpegAudioVersion.Mpeg25 => 576,
                _ => throw new InvalidOperationException()
            };
            ChannelMode = ((word >> 6) & 3) switch
            {
                0 => Mp3ChannelMode.Stereo,
                1 => Mp3ChannelMode.JointStereo,
                2 => Mp3ChannelMode.DualChannel,
                3 => Mp3ChannelMode.Mono,
                _ => throw new InvalidOperationException()
            };
            ChannelCount = ChannelMode switch
            {
                Mp3ChannelMode.Stereo or Mp3ChannelMode.JointStereo or Mp3ChannelMode.DualChannel => 2,
                Mp3ChannelMode.Mono => 1,
                _ => throw new InvalidOperationException()
            };
            SideInfoBytes = (Version, ChannelCount) switch
            {
                (MpegAudioVersion.Mpeg1, 1) => 17,
                (MpegAudioVersion.Mpeg1, 2) => 32,
                (MpegAudioVersion.Mpeg2 or MpegAudioVersion.Mpeg25, 1) => 9,
                (MpegAudioVersion.Mpeg2 or MpegAudioVersion.Mpeg25, 2) => 17,
                _ => throw new InvalidOperationException()
            };
            HasCrc = (word & 0x10000) == 0;
            HasPadding = (word & 0x200) != 0;
            ModeExtension = (int)((word >> 4) & 3);
            Emphasis = (int)(word & 3);
            FrameBytes = SamplesPerFrame / 8 * BitRate / SampleRate + (HasPadding ? 1 : 0);
            if (FrameBytes < HeaderBytes + SideInfoBytes + (HasCrc ? 2 : 0))
            {
                throw new InvalidDataException("The MP3 frame cannot contain its header, CRC and side information.");
            }
        }

        public MpegAudioVersion Version { get; }
        public Mp3ChannelMode ChannelMode { get; }
        public int BitRate { get; }
        public int SampleRate { get; }
        public int ChannelCount { get; }
        public int SamplesPerFrame { get; }
        public int FrameBytes { get; }
        public int SideInfoBytes { get; }
        public int ModeExtension { get; }
        public int Emphasis { get; }
        public bool HasCrc { get; }
        public bool HasPadding { get; }

        public static Mp3FrameHeader Read(ReadOnlySpan<byte> data)
        {
            if (data.Length != HeaderBytes)
            {
                throw new InvalidDataException("An MP3 frame header must contain exactly four bytes.");
            }
            return new Mp3FrameHeader(BinaryPrimitives.ReadUInt32BigEndian(data));
        }

        public static bool HasLayer3Sync(ReadOnlySpan<byte> data) => data.Length >= HeaderBytes
            && data[0] == 0xff && (data[1] & 0xe6) == 0xe2 && (data[1] & 0x18) != 8;
    }
}
