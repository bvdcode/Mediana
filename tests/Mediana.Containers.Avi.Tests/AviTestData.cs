// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Containers.Avi.Tests
{
    internal static class AviTestData
    {
        public static byte[] Build(AviIndexKind kind, bool records = false, uint videoStart = 3, bool dropped = false)
        {
            byte[] videoHeader = new byte[56];
            Encoding.ASCII.GetBytes("vidsMJPG").CopyTo(videoHeader, 0);
            Put(videoHeader, 20, 1001); Put(videoHeader, 24, 30000); Put(videoHeader, 28, videoStart); Put(videoHeader, 32, dropped ? 4u : 3u);
            byte[] videoFormat = new byte[40];
            Put(videoFormat, 0, 40); Put(videoFormat, 4, 16); Put(videoFormat, 8, 16);
            BinaryPrimitives.WriteUInt16LittleEndian(videoFormat.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(videoFormat.AsSpan(14), 24);
            Encoding.ASCII.GetBytes("MJPG").CopyTo(videoFormat, 16);
            byte[] main = new byte[56];
            Put(main, 0, 33367); Put(main, 16, dropped ? 4u : 3u); Put(main, 24, 1); Put(main, 32, 16); Put(main, 36, 16);
            bool openDml = kind is AviIndexKind.Standard or AviIndexKind.Super;
            byte[] picture = [0xff, 0xd8, 1, 2, 0xff, 0xd9, 9];
            List<byte[]> packets = [Chunk("00dc", picture), Chunk("00dc", picture), Chunk("00dc", picture)];
            if (dropped) { packets.Insert(1, Chunk("00dc", [])); }
            byte[] moviePayload = records ? Chunk("LIST", Join("rec ", [.. packets])) : Join("", [.. packets]);
            byte[] movie = Chunk("LIST", Join("movi", moviePayload));
            byte[] standard = new byte[24 + packets.Count * 8];
            BinaryPrimitives.WriteUInt16LittleEndian(standard, 2); standard[3] = 1;
            Put(standard, 4, (uint)packets.Count); Encoding.ASCII.GetBytes("00dc").CopyTo(standard, 8);
            byte[] super = new byte[40];
            BinaryPrimitives.WriteUInt16LittleEndian(super, 4);
            Put(super, 4, 1); Encoding.ASCII.GetBytes("00dc").CopyTo(super, 8);
            byte[] Header(byte[] index)
            {
                byte[] stream = Join("strl", Chunk("strh", videoHeader), Chunk("strf", videoFormat), index);
                return Chunk("LIST", Join("hdrl", Chunk("avih", main), Chunk("LIST", stream)));
            }
            byte[] indexHeader = kind switch
            {
                AviIndexKind.Standard => Chunk("indx", standard),
                AviIndexKind.Super => Chunk("indx", super),
                AviIndexKind.Relative or AviIndexKind.Absolute or AviIndexKind.None => [],
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            byte[] header = Header(indexHeader);
            int movieFourcc = 12 + header.Length + 8;
            int firstHeader = movieFourcc + 4 + (records ? 12 : 0);
            byte[] old = new byte[packets.Count * 16];
            int address = firstHeader;
            for (int i = 0; i < packets.Count; i++)
            {
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(packets[i].AsSpan(4));
                Encoding.ASCII.GetBytes("00dc").CopyTo(old, i * 16); Put(old, i * 16 + 4, 16);
                Put(old, i * 16 + 8, (uint)(kind == AviIndexKind.Absolute ? address : address - movieFourcc)); Put(old, i * 16 + 12, size);
                Put(standard, 24 + i * 8, (uint)(address + 8)); Put(standard, 28 + i * 8, size);
                address += packets[i].Length;
            }
            byte[] tail = [];
            if (kind == AviIndexKind.Super)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(super.AsSpan(24), (ulong)(12 + header.Length + movie.Length));
                Put(super, 32, (uint)(standard.Length + 8)); Put(super, 36, (uint)packets.Count);
                header = Header(Chunk("indx", super)); tail = Chunk("ix00", standard);
            }
            else if (kind == AviIndexKind.Standard) { header = Header(Chunk("indx", standard)); }
            else if (!openDml && kind != AviIndexKind.None) { tail = Chunk("idx1", old); }
            return Chunk("RIFF", Join("AVI ", header, movie, tail));
        }

        public static byte[] BuildAvix()
        {
            byte[] original = Build(AviIndexKind.Super);
            int index = original.AsSpan().IndexOf("indx"u8);
            int insert = index + 48;
            byte[] first = new byte[original.Length + 16];
            original.AsSpan(0, insert).CopyTo(first);
            original.AsSpan(insert).CopyTo(first.AsSpan(insert + 16));
            Put(first, 4, (uint)(first.Length - 8));
            Put(first, 16, BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(16)) + 16);
            int stream = first.AsSpan().IndexOf("strl"u8) - 8;
            Put(first, stream + 4, BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(stream + 4)) + 16);
            Put(first, index + 4, 56);
            Put(first, index + 12, 2);
            int standard = original.AsSpan().IndexOf("ix00"u8) + 16;
            BinaryPrimitives.WriteUInt64LittleEndian(first.AsSpan(index + 32), (ulong)standard);
            for (int packet = 0; packet < 3; packet++)
            {
                int entry = standard + 32 + packet * 8;
                Put(first, entry, BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(entry)) + 16);
            }
            byte[] movie = Chunk("LIST", Join("movi", Chunk("00dc", [0xff, 0xd8, 1, 2, 0xff, 0xd9, 9])));
            byte[] last = new byte[32];
            BinaryPrimitives.WriteUInt16LittleEndian(last, 2); last[3] = 1;
            Put(last, 4, 1); Encoding.ASCII.GetBytes("00dc").CopyTo(last, 8);
            Put(last, 24, (uint)(first.Length + 12 + 12 + 8)); Put(last, 28, 7);
            byte[] continuation = Chunk("RIFF", Join("AVIX", movie, Chunk("ix00", last)));
            BinaryPrimitives.WriteUInt64LittleEndian(first.AsSpan(index + 48), (ulong)(first.Length + 12 + movie.Length));
            Put(first, index + 56, (uint)(last.Length + 8)); Put(first, index + 60, 1);
            int main = first.AsSpan().IndexOf("avih"u8);
            Put(first, main + 24, 4);
            int header = first.AsSpan().IndexOf("strh"u8);
            Put(first, header + 40, 4);
            return Join("", first, continuation);
        }

        private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

        private static byte[] Chunk(string id, byte[] data)
        {
            byte[] output = new byte[8 + data.Length + (data.Length & 1)];
            Encoding.ASCII.GetBytes(id).CopyTo(output, 0); Put(output, 4, (uint)data.Length); data.CopyTo(output, 8);
            return output;
        }

        private static byte[] Join(string type, params byte[][] arrays)
        {
            byte[] output = new byte[type.Length + arrays.Sum(item => item.Length)];
            Encoding.ASCII.GetBytes(type).CopyTo(output, 0);
            int offset = type.Length;
            foreach (byte[] array in arrays) { array.CopyTo(output, offset); offset += array.Length; }
            return output;
        }
    }
}
