// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    /// <summary>Bounded E-AC-3 BSI prefix containing compression presence and dependent channel mapping.
    /// A maximum of twelve bytes suffices, including both compression words in dual-mono mode.</summary>
    public readonly record struct Eac3FrameMetadata(Ac3FrameHeader Header, byte? Compression, byte? SecondaryCompression, ushort? ChannelMap)
    {
        public const int MaximumPrefixBytes = 12;
        public string[] GetChannelOrder() => ChannelMap is ushort map ? Eac3ChannelMap.GetChannelOrder(map) : Header.GetChannelOrder();

        public static Eac3FrameMetadata Read(ReadOnlySpan<byte> data)
        {
            return Read(data, Ac3FrameHeader.Read(data));
        }

        internal static Eac3FrameMetadata Read(ReadOnlySpan<byte> data, Ac3FrameHeader header)
        {
            if (!header.IsEnhanced) { throw new InvalidDataException("E-AC-3 BSI metadata requires an enhanced syncframe."); }
            int position = 45;
            Read(data, ref position, 5);
            byte? compression = Read(data, ref position, 1) == 0 ? null : (byte)Read(data, ref position, 8);
            byte? secondary = null;
            if (header.ChannelMode == 0)
            {
                Read(data, ref position, 5);
                if (Read(data, ref position, 1) != 0) { secondary = (byte)Read(data, ref position, 8); }
            }
            ushort? map = null;
            if (header.StreamType == 1 && Read(data, ref position, 1) != 0)
            {
                map = (ushort)Read(data, ref position, 16);
                if (Eac3ChannelMap.GetChannelCount(map.Value) != header.ChannelCount)
                { throw new InvalidDataException("The E-AC-3 dependent channel map disagrees with its coded channel count."); }
            }
            return new(header, compression, secondary, map);
        }

        private static int Read(ReadOnlySpan<byte> data, ref int position, int count)
        {
            if (count > data.Length * 8 - position) { throw new InvalidDataException("The E-AC-3 BSI metadata prefix is truncated."); }
            int value = 0;
            for (int bit = 0; bit < count; bit++, position++)
            { value = value << 1 | data[position >> 3] >> (7 - (position & 7)) & 1; }
            return value;
        }
    }
}
