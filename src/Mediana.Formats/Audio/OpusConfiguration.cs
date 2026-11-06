// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Audio
{
    /// <summary>Opus identification fields and channel mapping. InputSampleRate is descriptive;
    /// the Ogg Opus media clock always uses 48 kHz. Pre-skip and output gain are retained exactly.</summary>
    public class OpusConfiguration
    {
        public const string CodecId = "opus";
        public const int ClockRate = 48000;
        public const int SeekPrerollSamples = 3840;
        public const int MinimumHeaderBytes = 19;

        private OpusConfiguration(ReadOnlySpan<byte> data)
        {
            Version = data[8];
            if (Version > 15) { throw new NotSupportedException("The Opus identification major version is not supported."); }
            ChannelCount = data[9];
            if (ChannelCount == 0) { throw new InvalidDataException("Opus requires at least one output channel."); }
            PreSkipSamples = BinaryPrimitives.ReadUInt16LittleEndian(data[10..]);
            InputSampleRate = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            OutputGainQ8 = BinaryPrimitives.ReadInt16LittleEndian(data[16..]);
            MappingFamily = data[18];
            int length;
            if (MappingFamily == 0)
            {
                if (ChannelCount > 2) { throw new InvalidDataException("Opus mapping family zero permits only mono or stereo."); }
                StreamCount = 1;
                CoupledStreamCount = ChannelCount - 1;
                ChannelMapping = ChannelCount switch
                {
                    1 => new byte[] { 0 },
                    2 => new byte[] { 0, 1 },
                    _ => throw new InvalidOperationException()
                };
                length = MinimumHeaderBytes;
            }
            else
            {
                length = MinimumHeaderBytes + 2 + ChannelCount;
                if (data.Length < length) { throw new InvalidDataException("The Opus channel mapping is truncated."); }
                StreamCount = data[19];
                CoupledStreamCount = data[20];
                if (StreamCount == 0 || CoupledStreamCount > StreamCount || StreamCount + CoupledStreamCount > 255)
                {
                    throw new InvalidDataException("The Opus coded stream and coupled stream counts are invalid.");
                }
                if (MappingFamily == 1 && ChannelCount > 8)
                {
                    throw new InvalidDataException("Opus mapping family one permits at most eight output channels.");
                }
                byte[] mapping = data.Slice(21, ChannelCount).ToArray();
                foreach (byte channel in mapping)
                {
                    if (channel != 255 && channel >= StreamCount + CoupledStreamCount)
                    {
                        throw new InvalidDataException("An Opus output channel refers to an absent coded channel.");
                    }
                }
                ChannelMapping = mapping;
            }
            if (Version <= 1 && data.Length != length)
            {
                throw new InvalidDataException("The Opus identification header has an invalid length.");
            }
        }

        public int Version { get; }
        public int ChannelCount { get; }
        public int PreSkipSamples { get; }
        public uint InputSampleRate { get; }
        public short OutputGainQ8 { get; }
        public double OutputGainDecibels => OutputGainQ8 / 256.0;
        public int MappingFamily { get; }
        public int StreamCount { get; }
        public int CoupledStreamCount { get; }
        public ReadOnlyMemory<byte> ChannelMapping { get; }

        public static OpusConfiguration Read(ReadOnlySpan<byte> data)
        {
            if (data.Length < MinimumHeaderBytes || !data[..8].SequenceEqual("OpusHead"u8))
            {
                throw new InvalidDataException("The Opus identification header is absent or truncated.");
            }
            return new(data);
        }
    }
}
