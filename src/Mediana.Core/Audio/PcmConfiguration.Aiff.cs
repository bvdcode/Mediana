// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Core.Audio
{
    public partial class PcmConfiguration
    {
        public const string AiffCodecId = "pcm.aiff";

        /// <summary>Reads the four-byte FORM type followed by the complete COMM payload.
        /// Returns null for an unsupported representation; malformed descriptions throw.</summary>
        public static PcmConfiguration? ReadAiffFormat(ReadOnlySpan<byte> configuration, out string? unsupportedReason)
        {
            unsupportedReason = null;
            if (configuration.Length < 22)
            {
                throw new InvalidDataException("The AIFF common chunk is truncated.");
            }
            bool compressed = configuration[..4].SequenceEqual("AIFC"u8);
            if (!compressed && !configuration[..4].SequenceEqual("AIFF"u8))
            {
                throw new InvalidDataException("The PCM configuration has an invalid AIFF form type.");
            }
            ReadOnlySpan<byte> common = configuration[4..];
            if ((!compressed && common.Length != 18) || (compressed && (common.Length < 24 ||
                common.Length != 22 + ((common[22] + 2) & ~1) || ((common[22] & 1) == 0 && common[^1] != 0))))
            {
                throw new InvalidDataException("The AIFF common chunk or its compression name has an invalid length.");
            }
            int channels = BinaryPrimitives.ReadInt16BigEndian(common);
            int precision = BinaryPrimitives.ReadInt16BigEndian(common[6..]);
            if (channels <= 0 || precision <= 0)
            {
                throw new InvalidDataException("The AIFF channel count and sample precision must be positive.");
            }
            int? rate = ReadAiffRate(common.Slice(8, 10));
            if (rate is null)
            {
                unsupportedReason = "AIFF requires a positive integral sample rate supported by the audio format.";
                return null;
            }
            if (channels > 2)
            {
                unsupportedReason = "AIFF PCM currently supports mono and stereo channel layouts.";
                return null;
            }
            uint compression = compressed ? BinaryPrimitives.ReadUInt32BigEndian(common[18..]) : 0x4e4f4e45;
            bool bigEndian;
            bool floating;
            switch (compression)
            {
                case 0x4e4f4e45: // NONE
                    bigEndian = true;
                    floating = false;
                    break;
                case 0x736f7774: // sowt
                case 0x74776f73: // twos
                    if (precision != 16)
                    {
                        unsupportedReason = "The AIFF-C sowt and twos representations require 16-bit samples.";
                        return null;
                    }
                    bigEndian = compression == 0x74776f73;
                    floating = false;
                    break;
                case 0x666c3332: // fl32
                case 0x666c3634: // fl64
                    int expected = compression == 0x666c3332 ? 32 : 64;
                    if (precision != expected)
                    {
                        throw new InvalidDataException("The AIFF-C float precision does not match its compression type.");
                    }
                    bigEndian = true;
                    floating = true;
                    break;
                default:
                    unsupportedReason = "The AIFF-C compression type is not supported PCM.";
                    return null;
            }
            if (!floating && precision > 32)
            {
                unsupportedReason = "AIFF integer PCM supports sample precision from 1 to 32 bits.";
                return null;
            }
            int storageBits = (precision + 7) / 8 * 8;
            AudioSampleFormat sampleFormat = (floating, storageBits) switch
            {
                (false, 8) => AudioSampleFormat.Unsigned8,
                (false, 16) => AudioSampleFormat.Signed16,
                (false, 24 or 32) => AudioSampleFormat.Signed32,
                (true, 32) => AudioSampleFormat.Float32,
                (true, 64) => AudioSampleFormat.Float64,
                _ => throw new InvalidDataException("The AIFF sample representation is invalid.")
            };
            string[] labels = channels switch
            {
                1 => ["mono"],
                2 => ["front-left", "front-right"],
                _ => throw new InvalidDataException("The AIFF channel count is invalid.")
            };
            return new PcmConfiguration(new AudioFormat(rate.Value, sampleFormat, AudioStorageLayout.Interleaved, labels),
                storageBits, precision, bigEndian, !floating && storageBits == 8);
        }

        private static int? ReadAiffRate(ReadOnlySpan<byte> extended)
        {
            int exponent = BinaryPrimitives.ReadUInt16BigEndian(extended);
            ulong mantissa = BinaryPrimitives.ReadUInt64BigEndian(extended[2..]);
            if ((exponent & 0x8000) != 0 || exponent == 0x7fff || mantissa == 0 ||
                (exponent != 0 && (mantissa & (1UL << 63)) == 0) || (exponent == 0 && (mantissa & (1UL << 63)) != 0))
            {
                throw new InvalidDataException("The AIFF extended sample rate is not a positive finite normalized value.");
            }
            int power = exponent - 16383;
            if (power is < 0 or > 30)
            {
                return null;
            }
            int shift = 63 - power;
            if ((mantissa & ((1UL << shift) - 1)) != 0)
            {
                return null;
            }
            return (int)(mantissa >> shift);
        }
    }
}
