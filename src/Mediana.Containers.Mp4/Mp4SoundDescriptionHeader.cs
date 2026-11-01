// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal static class Mp4SoundDescriptionHeader
    {
        public static (ushort Version, int Length, int Channels, int SampleRate) Read(ReadOnlySpan<byte> data)
        {
            Mp4Binary.Range(data, 0, 28);
            ushort version = Mp4Binary.UInt16(data, 8);
            int length = version switch
            {
                0 => 28,
                1 => 44,
                2 => 64,
                _ => throw new NotSupportedException($"QuickTime sound sample description version {version} is not supported.")
            };
            Mp4Binary.Range(data, 0, length);
            switch (version)
            {
                case 0:
                    break;
                case 1:
                    if (Mp4Binary.UInt16(data, 20) != 0xfffe)
                    {
                        throw new NotSupportedException("QuickTime compressed audio requires redefined sample tables (compression ID -2).");
                    }
                    if (Mp4Binary.UInt32(data, 32) != 0 || Mp4Binary.UInt32(data, 36) != 0)
                    {
                        throw new InvalidDataException("QuickTime variable-rate audio reserves bytes-per-packet and bytes-per-frame as zero.");
                    }
                    break;
                case 2:
                    return ReadVersion2(data);
                default:
                    throw new NotSupportedException($"QuickTime sound sample description version {version} is not supported.");
            }
            int channels = Mp4Binary.UInt16(data, 16);
            uint rate = Mp4Binary.UInt32(data, 24);
            if (channels == 0 || (rate & 0xffff) != 0)
            {
                throw new NotSupportedException("The MP4 audio sample declaration is not supported.");
            }
            return (version, length, channels, (int)(rate >> 16));
        }

        private static (ushort Version, int Length, int Channels, int SampleRate) ReadVersion2(ReadOnlySpan<byte> data)
        {
            if (Mp4Binary.UInt16(data, 16) != 3 || Mp4Binary.UInt16(data, 18) != 16 ||
                Mp4Binary.UInt16(data, 20) != 0xfffe || Mp4Binary.UInt16(data, 22) != 0 ||
                Mp4Binary.UInt32(data, 24) != 65536 || Mp4Binary.UInt32(data, 44) != 0x7f000000)
            {
                throw new InvalidDataException("The QuickTime version 2 sound description constants are invalid.");
            }
            if (Mp4Binary.UInt32(data, 28) != 72)
            {
                throw new NotSupportedException("The QuickTime version 2 sound structure size is not supported.");
            }
            if (Mp4Binary.UInt32(data, 48) != 0 || Mp4Binary.UInt32(data, 52) != 0 || Mp4Binary.UInt32(data, 56) != 0)
            {
                throw new NotSupportedException("QuickTime variable-rate MPEG audio requires zero PCM flags and constant packet-byte count.");
            }
            double rate = BitConverter.Int64BitsToDouble(Mp4Binary.Int64(data, 32));
            uint channels = Mp4Binary.UInt32(data, 40);
            if (!double.IsFinite(rate) || rate <= 0 || rate > int.MaxValue || rate != Math.Truncate(rate) || channels == 0 || channels > int.MaxValue)
            {
                throw new NotSupportedException("QuickTime sound declarations require a positive integral sample rate and channel count in the supported range.");
            }
            return (2, 64, (int)channels, (int)rate);
        }
    }
}
