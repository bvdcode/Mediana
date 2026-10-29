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
            int start = payload - 8;
            int oldSize = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(start));
            Assert.True(Mp4TestData.PayloadOffset(data, "moov") > Mp4TestData.PayloadOffset(data, "mdat"));
            byte[] replacement = new byte[20];
            BinaryPrimitives.WriteUInt32BigEndian(replacement, (uint)replacement.Length);
            "stsz"u8.CopyTo(replacement.AsSpan(4));
            BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(12), size);
            BinaryPrimitives.WriteUInt32BigEndian(replacement.AsSpan(16), BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(payload + 8)));
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
