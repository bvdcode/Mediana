// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal readonly record struct Mp4TrackFragmentHeader(uint TrackId, long BaseOffset,
        Mp4FragmentDefaults Defaults, bool DurationIsEmpty)
    {
        public static Mp4TrackFragmentHeader Read(ReadOnlySpan<byte> data, Mp4FragmentDefaults defaults,
            long movieOffset, long implicitOffset)
        {
            Mp4Binary.FullBox(data, allowFlags: true);
            uint flags = Mp4Binary.UInt32(data, 0);
            if ((flags & ~0x03003Bu) != 0)
            {
                throw new NotSupportedException("The MP4 track fragment header flags are unsupported.");
            }
            uint track = Mp4Binary.UInt32(data, 4);
            int offset = 8;
            long baseOffset;
            if ((flags & 1) != 0)
            {
                baseOffset = Mp4Binary.Offset64(Mp4Binary.UInt64(data, offset));
                offset += 8;
            }
            else
            {
                baseOffset = (flags & 0x020000) != 0 ? movieOffset : implicitOffset;
            }
            uint description = ReadOptional(data, flags, 2, ref offset, defaults.DescriptionIndex);
            uint duration = ReadOptional(data, flags, 8, ref offset, defaults.Duration);
            uint size = ReadOptional(data, flags, 16, ref offset, defaults.Size);
            uint sampleFlags = ReadOptional(data, flags, 32, ref offset, defaults.Flags);
            if (offset != data.Length)
            {
                throw new InvalidDataException("The MP4 track fragment header length is invalid.");
            }
            if (description != 1)
            {
                throw new NotSupportedException("MP4 sample description changes are not supported.");
            }
            return new(track, baseOffset, new(description, duration, size, sampleFlags), (flags & 0x010000) != 0);
        }

        private static uint ReadOptional(ReadOnlySpan<byte> data, uint flags, uint mask, ref int offset, uint inherited)
        {
            if ((flags & mask) == 0)
            {
                return inherited;
            }
            uint value = Mp4Binary.UInt32(data, offset);
            offset += 4;
            return value;
        }
    }
}
