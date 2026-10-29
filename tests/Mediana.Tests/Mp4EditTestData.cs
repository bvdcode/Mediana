// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    internal static class Mp4EditTestData
    {
        public static byte[] Replace(byte[] data, bool wide, params (long Duration, long MediaTime, int Rate)[] edits)
        {
            int payload = Mp4TestData.PayloadOffset(data, "elst");
            int start = payload - 8;
            int oldSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start));
            Assert.True(Mp4TestData.PayloadOffset(data, "moov") > Mp4TestData.PayloadOffset(data, "mdat"));
            int entrySize = wide ? 20 : 12;
            byte[] replacement = new byte[16 + entrySize * edits.Length];
            BinaryPrimitives.WriteUInt32BigEndian(replacement, (uint)replacement.Length);
            "elst"u8.CopyTo(replacement.AsSpan(4));
            replacement[8] = wide ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(12), (uint)edits.Length);
            for (int index = 0; index < edits.Length; index++)
            {
                (long duration, long mediaTime, int rate) = edits[index];
                Span<byte> entry = replacement.AsSpan(16 + index * entrySize, entrySize);
                if (wide)
                {
                    BinaryPrimitives.WriteUInt64BigEndian(entry, checked((ulong)duration));
                    BinaryPrimitives.WriteInt64BigEndian(entry[8..], mediaTime);
                    BinaryPrimitives.WriteInt32BigEndian(entry[16..], rate);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32BigEndian(entry, checked((uint)duration));
                    BinaryPrimitives.WriteInt32BigEndian(entry[4..], checked((int)mediaTime));
                    BinaryPrimitives.WriteInt32BigEndian(entry[8..], rate);
                }
            }
            int delta = replacement.Length - oldSize;
            foreach (string parent in new[] { "moov", "trak", "edts" })
            {
                int parentOffset = Mp4TestData.PayloadOffset(data, parent) - 8;
                uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(parentOffset));
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(parentOffset), checked((uint)(size + delta)));
            }
            return [.. data.AsSpan(0, start), .. replacement, .. data.AsSpan(start + oldSize)];
        }
    }
}
