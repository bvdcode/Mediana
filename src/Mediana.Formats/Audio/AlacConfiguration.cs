// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Formats.Audio
{
    /// <summary>The big-endian ALACSpecificConfig supplied by the containing file.</summary>
    public record AlacConfiguration(int FrameLength, int BitDepth, int HistoryMultiplier, int InitialHistory,
        int MaximumRiceParameter, int ChannelCount, uint MaximumFrameBytes, int SampleRate)
    {
        public const string CodecId = "alac";
        public const int ByteLength = 24;
        public uint? ChannelLayoutTag { get; init; }

        public string[] GetChannelOrder() => ChannelLayoutTag switch
        {
            null when ChannelCount == 1 => ["front-center"],
            null when ChannelCount == 2 => ["front-left", "front-right"],
            null => Enumerable.Range(0, ChannelCount).Select(index => $"discrete-{index}").ToArray(),
            (100u << 16) | 1 => ["front-center"],
            (101u << 16) | 2 => ["front-left", "front-right"],
            (113u << 16) | 3 => ["front-center", "front-left", "front-right"],
            (116u << 16) | 4 => ["front-center", "front-left", "front-right", "back-center"],
            (120u << 16) | 5 => ["front-center", "front-left", "front-right", "back-left", "back-right"],
            (124u << 16) | 6 => ["front-center", "front-left", "front-right", "back-left", "back-right", "low-frequency"],
            (142u << 16) | 7 => ["front-center", "front-left", "front-right", "side-left", "side-right", "back-center", "low-frequency"],
            (127u << 16) | 8 => ["front-center", "front-left-center", "front-right-center", "front-left", "front-right", "back-left", "back-right", "low-frequency"],
            _ => throw new NotSupportedException("The ALAC channel layout tag is unsupported.")
        };

        public static AlacConfiguration Read(ReadOnlySpan<byte> data)
        {
            if (data.Length is not (ByteLength or 48))
            {
                throw new InvalidDataException("ALAC requires its 24-byte configuration and an optional 24-byte channel layout.");
            }
            uint frames = BinaryPrimitives.ReadUInt32BigEndian(data);
            uint rate = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
            if (frames == 0 || frames > int.MaxValue / 32 || rate == 0 || rate > int.MaxValue ||
                data[9] is < 1 or > 8 || data[8] is < 1 or > 31)
            {
                throw new InvalidDataException("The ALAC frame length, sample rate, channel count or Rice parameter is invalid.");
            }
            if (data[4] != 0 || data[5] is not (16 or 20 or 24 or 32))
            {
                throw new NotSupportedException("ALAC requires version zero and 16, 20, 24 or 32-bit samples.");
            }
            uint? layout = null;
            if (data.Length == 48)
            {
                if (BinaryPrimitives.ReadUInt32BigEndian(data[24..]) != 24 || !data.Slice(28, 4).SequenceEqual("chan"u8) ||
                    BinaryPrimitives.ReadUInt32BigEndian(data[32..]) != 0 || BinaryPrimitives.ReadUInt64BigEndian(data[40..]) != 0)
                {
                    throw new InvalidDataException("The ALAC channel layout record is invalid.");
                }
                layout = BinaryPrimitives.ReadUInt32BigEndian(data[36..]);
                if ((layout & 65535) != data[9])
                {
                    throw new InvalidDataException("The ALAC channel layout count disagrees with its configuration.");
                }
            }
            return new((int)frames, data[5], data[6], data[7], data[8], data[9],
                BinaryPrimitives.ReadUInt32BigEndian(data[12..]), (int)rate)
            { ChannelLayoutTag = layout };
        }
    }
}
