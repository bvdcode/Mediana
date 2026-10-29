// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Audio;

namespace Mediana.Processing.Audio
{
    internal static class PcmSampleAccess
    {
        public static double Read(AudioFrame frame, int sample, int channel)
        {
            AudioFormat format = frame.Format;
            (int plane, int offset) = Locate(format, sample, channel);
            ReadOnlySpan<byte> bytes = frame.GetPlane(plane).Span[offset..];
            double value = format.SampleFormat switch
            {
                AudioSampleFormat.Unsigned8 => (bytes[0] - 128) / 128d,
                AudioSampleFormat.Signed16 => MemoryMarshal.Read<short>(bytes) / 32768d,
                AudioSampleFormat.Signed32 => MemoryMarshal.Read<int>(bytes) / 2147483648d,
                AudioSampleFormat.Float32 => MemoryMarshal.Read<float>(bytes),
                AudioSampleFormat.Float64 => MemoryMarshal.Read<double>(bytes),
                _ => throw new ArgumentOutOfRangeException(nameof(frame))
            };
            if (!double.IsFinite(value))
            {
                throw new InvalidDataException("Audio processing requires finite PCM samples.");
            }
            return value;
        }

        public static void Write(Span<byte> bytes, AudioSampleFormat format, double value)
        {
            if (!double.IsFinite(value))
            {
                throw new InvalidDataException("Audio processing produced a non-finite sample.");
            }
            switch (format)
            {
                case AudioSampleFormat.Unsigned8:
                    bytes[0] = (byte)Math.Clamp(Math.Round(Math.Clamp(value, -1, 1) * 128 + 128), byte.MinValue, byte.MaxValue);
                    break;
                case AudioSampleFormat.Signed16:
                    short signed16 = (short)Math.Clamp(Math.Round(Math.Clamp(value, -1, 1) * 32768), short.MinValue, short.MaxValue);
                    MemoryMarshal.Write(bytes, in signed16);
                    break;
                case AudioSampleFormat.Signed32:
                    int signed32 = (int)Math.Clamp(Math.Round(Math.Clamp(value, -1, 1) * 2147483648d), int.MinValue, int.MaxValue);
                    MemoryMarshal.Write(bytes, in signed32);
                    break;
                case AudioSampleFormat.Float32:
                    float single = (float)value;
                    if (!float.IsFinite(single))
                    {
                        throw new InvalidDataException("The PCM sample exceeds finite float32 storage.");
                    }
                    MemoryMarshal.Write(bytes, in single);
                    break;
                case AudioSampleFormat.Float64:
                    MemoryMarshal.Write(bytes, in value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
        }

        public static void RequireFormat(AudioFormat actual, AudioFormat expected)
        {
            if (actual.SampleRate != expected.SampleRate || actual.SampleFormat != expected.SampleFormat
                || actual.Layout != expected.Layout || !actual.Channels.SequenceEqual(expected.Channels))
            {
                throw new NotSupportedException("The audio format or channel order changed during processing.");
            }
        }

        private static (int Plane, int Offset) Locate(AudioFormat format, int sample, int channel) => format.Layout switch
        {
            AudioStorageLayout.Interleaved => (0, checked((sample * format.Channels.Count + channel) * format.BytesPerSample)),
            AudioStorageLayout.Planar => (channel, checked(sample * format.BytesPerSample)),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
    }
}
