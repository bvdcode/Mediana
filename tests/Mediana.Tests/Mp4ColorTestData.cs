// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Tests
{
    internal static class Mp4ColorTestData
    {
        public static string PathFor(string name = "container-709-limited.mp4") =>
            Path.Combine(AppContext.BaseDirectory, "Data", "Preview", name);

        public static byte[] Cicp(string type = "nclx", ushort primaries = 1, ushort transfer = 1, ushort matrix = 1, byte range = 0)
        {
            byte[] payload = new byte[type == "nclx" ? 11 : 10];
            Encoding.Latin1.GetBytes(type).CopyTo(payload.AsSpan(0, 4));
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), primaries);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), transfer);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), matrix);
            if (type == "nclx")
            {
                payload[10] = range;
            }
            return payload;
        }

        public static async Task<byte[]> ReplaceAsync(params byte[][] payloads)
        {
            byte[] file = await File.ReadAllBytesAsync(PathFor());
            List<int> parents = [];
            int start = 0;
            int end = file.Length;
            foreach ((string type, int header) in new[] { ("moov", 8), ("trak", 8), ("mdia", 8), ("minf", 8),
                ("stbl", 8), ("stsd", 16), ("avc1", 86) })
            {
                int box = Find(file, start, end, type);
                parents.Add(box);
                start = box + header;
                end = box + (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(box));
            }
            int originalStart = Find(file, start, end, "colr");
            int originalSize = (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(originalStart));
            byte[] replacement = payloads.SelectMany(payload => Mp4MetadataTestData.Box("colr", payload)).ToArray();
            int delta = replacement.Length - originalSize;
            Assert.True(Find(file, 0, file.Length, "mdat") < parents[0]);
            foreach (int parent in parents)
            {
                uint length = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(parent));
                BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(parent), (uint)(length + delta));
            }
            return [.. file.AsSpan(0, originalStart), .. replacement, .. file.AsSpan(originalStart + originalSize)];
        }

        private static int Find(byte[] file, int start, int end, string type)
        {
            for (int offset = start; offset < end; offset += (int)BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(offset)))
            {
                if (Encoding.Latin1.GetString(file.AsSpan(offset + 4, 4)) == type)
                {
                    return offset;
                }
            }
            throw new InvalidDataException($"The test input has no {type} box.");
        }
    }
}
