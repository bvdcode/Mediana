// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Images.Heif.Container
{
    internal static class HeifFileType
    {
        internal static bool ContainsBrand(ReadOnlySpan<byte> prefix, ReadOnlySpan<uint> brands)
        {
            if (prefix.Length < 16 || !prefix.Slice(4, 4).SequenceEqual("ftyp"u8)) { return false; }
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(prefix);
            int header = 8;
            if (size == 1)
            {
                if (prefix.Length < 24) { return false; }
                size = BinaryPrimitives.ReadUInt64BigEndian(prefix[8..]); header = 16;
            }
            if (size == 0) { size = (ulong)prefix.Length; }
            if (size < (ulong)header + 8 || (size - (ulong)header) % 4 != 0) { return false; }
            int end = (int)Math.Min(size, (ulong)prefix.Length);
            for (int offset = header; offset <= end - 4; offset += 4)
            {
                if (offset != header + 4 && brands.Contains(BinaryPrimitives.ReadUInt32BigEndian(prefix[offset..]))) { return true; }
            }
            return false;
        }
    }
}
