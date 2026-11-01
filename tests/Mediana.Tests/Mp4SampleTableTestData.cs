// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    internal static class Mp4SampleTableTestData
    {
        public static byte[] WithConstantSampleSize(byte[] data, uint size)
        {
            int payload = Mp4TestData.PayloadOffset(data, "stsz");
            byte[] replacement = new byte[20];
            BinaryPrimitives.WriteUInt32BigEndian(replacement, (uint)replacement.Length);
            "stsz"u8.CopyTo(replacement.AsSpan(4));
            BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(12), size);
            BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(16), BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payload + 8)));
            return ReplaceTable(data, "stsz", replacement);
        }

        public static byte[] WithTimingRuns(byte[] data, params (uint Count, uint Duration)[] runs)
        {
            byte[] replacement = new byte[16 + runs.Length * 8];
            BinaryPrimitives.WriteUInt32BigEndian(replacement, (uint)replacement.Length);
            "stts"u8.CopyTo(replacement.AsSpan(4));
            BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(12), (uint)runs.Length);
            for (int index = 0; index < runs.Length; index++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(16 + index * 8), runs[index].Count);
                BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(20 + index * 8), runs[index].Duration);
            }
            return ReplaceTable(data, "stts", replacement);
        }

        private static byte[] ReplaceTable(byte[] data, string type, byte[] replacement)
        {
            int start = Mp4TestData.PayloadOffset(data, type) - 8;
            int oldSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start));
            Assert.True(Mp4TestData.PayloadOffset(data, "moov") > Mp4TestData.PayloadOffset(data, "mdat"));
            foreach (string parent in new[] { "moov", "trak", "mdia", "minf", "stbl" })
            {
                int offset = Mp4TestData.PayloadOffset(data, parent) - 8;
                uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), checked((uint)(length + replacement.Length - oldSize)));
            }
            return [.. data.AsSpan(0, start), .. replacement, .. data.AsSpan(start + oldSize)];
        }
    }
}
