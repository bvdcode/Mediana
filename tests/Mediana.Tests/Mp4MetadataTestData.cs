// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Tests
{
    internal static class Mp4MetadataTestData
    {
        public static byte[] Box(string type, params byte[][] payloads)
        {
            byte[] result = new byte[8 + payloads.Sum(payload => payload.Length)];
            BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
            Encoding.Latin1.GetBytes(type).CopyTo(result.AsSpan(4, 4));
            int offset = 8;
            foreach (byte[] payload in payloads)
            {
                payload.CopyTo(result.AsSpan(offset));
                offset += payload.Length;
            }
            return result;
        }

        public static byte[] Data(uint type, uint locale, byte[] value)
        {
            byte[] header = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(header, type);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), locale);
            return Box("data", header, value);
        }

        public static byte[] Meta(string handler, byte[] list, byte[]? keys = null)
        {
            byte[] fields = new byte[24];
            Encoding.Latin1.GetBytes(handler).CopyTo(fields.AsSpan(8, 4));
            if (keys is not null)
            {
                return Box("meta", new byte[4], Box("hdlr", fields), keys, Box("ilst", list));
            }
            return Box("meta", new byte[4], Box("hdlr", fields), Box("ilst", list));
        }

        public static async Task<byte[]> FileAsync(byte[] metadata)
        {
            byte[] original = await Mp4TestData.ReadAsync("avc-tail.mov");
            int start = Mp4TestData.PayloadOffset(original, "moov") - 8;
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(start));
            List<byte[]> children = [];
            int position = start + 8;
            while (position < start + size)
            {
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(original.AsSpan(position));
                string type = Encoding.Latin1.GetString(original.AsSpan(position + 4, 4));
                if (type != "udta" && type != "meta")
                {
                    children.Add(original.AsSpan(position, length).ToArray());
                }
                position += length;
            }
            children.Add(metadata);
            byte[] movie = Box("moov", children.ToArray());
            return [.. original.AsSpan(0, start), .. movie, .. original.AsSpan(start + size)];
        }

        public static byte[] AppendTo(byte[] file, byte[] addition, params string[] path)
        {
            Assert.True(Mp4TestData.PayloadOffset(file, "moov") > Mp4TestData.PayloadOffset(file, "mdat"));
            return Append(file, addition, path);
        }

        private static byte[] Append(ReadOnlySpan<byte> boxes, byte[] addition, ReadOnlySpan<string> path)
        {
            if (path.IsEmpty)
            {
                return [.. boxes, .. addition];
            }
            int offset = 0;
            while (offset < boxes.Length)
            {
                int size = (int)BinaryPrimitives.ReadUInt32BigEndian(boxes[offset..]);
                string type = Encoding.Latin1.GetString(boxes.Slice(offset + 4, 4));
                if (type == path[0])
                {
                    byte[] replacement = Box(type, Append(boxes.Slice(offset + 8, size - 8), addition, path[1..]));
                    return [.. boxes[..offset], .. replacement, .. boxes[(offset + size)..]];
                }
                offset += size;
            }
            throw new InvalidDataException("The test metadata scope was not found.");
        }
    }
}
