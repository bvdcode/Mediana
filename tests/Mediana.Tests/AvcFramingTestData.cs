// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Tests
{
    internal static class AvcFramingTestData
    {
        public static byte[] Packet(int lengthSize, params byte[][] nals)
        {
            using MemoryStream bytes = new();
            Span<byte> length = stackalloc byte[4];
            foreach (byte[] nal in nals)
            {
                BinaryPrimitives.WriteInt32BigEndian(length, nal.Length);
                bytes.Write(length[(4 - lengthSize)..]);
                bytes.Write(nal);
            }
            return bytes.ToArray();
        }

        public static List<byte[]> Nals(ReadOnlySpan<byte> output)
        {
            List<byte[]> result = [];
            while (!output.IsEmpty)
            {
                Assert.Equal(new byte[] { 0, 0, 0, 1 }, output[..4].ToArray());
                output = output[4..];
                int next = output.IndexOf(new byte[] { 0, 0, 0, 1 });
                int count = next < 0 ? output.Length : next;
                result.Add(output[..count].ToArray());
                output = output[count..];
            }
            return result;
        }
    }
}
