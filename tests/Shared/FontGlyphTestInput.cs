// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;

namespace Mediana.Tests
{
    internal static class FontGlyphTestInput
    {
        public static void VerifyRejectedAllocation()
        {
            byte[] glyph = new byte[10];
            BinaryPrimitives.WriteInt16BigEndian(glyph, short.MaxValue);
            TrueTypeFont font = new(Create(glyph));
            Reject(font, 0);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int repeat = 0; repeat < 3; repeat++)
            {
                Reject(font, 0);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            if (allocated >= 3 * 8192)
            {
                throw new InvalidDataException($"Truncated glyph parsing allocated {allocated} bytes before rejecting missing contour data.");
            }
        }

        public static void VerifyTruncatedGlyphs()
        {
            byte[] valid = Triangle();
            for (int length = 1; length < valid.Length; length++)
            {
                Reject(new(Create(valid[..length], valid)), 0);
            }
            byte[] composite = Composite();
            for (int length = 1; length < composite.Length; length++)
            {
                Reject(new(Create(composite[..length], valid)), 0);
            }
        }

        public static void VerifyValidOutlines()
        {
            TrueTypeFont font = new(Create(Composite(), Triangle()));
            VectorPath path = font.Outline(0);
            if (path.CommandCount != 4 || path.Elements[0].First.X != 20 || path.Elements[0].First.Y != 30 ||
                path.Elements[1].First.X != 120 || path.Elements[1].First.Y != 30 ||
                path.Elements[2].First.X != 20 || path.Elements[2].First.Y != 130)
            {
                throw new InvalidDataException("Composite glyph translation changed the independently defined triangle.");
            }
            path.LineTo(500, 600);
            if (font.Outline(0).CommandCount != 4)
            {
                throw new InvalidDataException("Returned outline mutation changed the cached glyph.");
            }
        }

        public static void VerifyEmptyOutlines()
        {
            TrueTypeFont font = new(Create(new byte[10], new byte[12], []));
            for (int glyph = 0; glyph < 3; glyph++)
            {
                if (font.Outline(glyph).CommandCount != 0)
                {
                    throw new InvalidDataException("A zero-contour glyph must have an empty outline.");
                }
            }
        }

        public static void VerifyCompositeInstructions()
        {
            byte[] glyph = new byte[26];
            Composite().CopyTo(glyph, 0);
            BinaryPrimitives.WriteUInt16BigEndian(glyph.AsSpan(10), 2 | 32 | 256);
            Composite().AsSpan(10, 6).CopyTo(glyph.AsSpan(16));
            BinaryPrimitives.WriteUInt16BigEndian(glyph.AsSpan(22), 2);
            glyph[24] = 0xaa;
            glyph[25] = 0xbb;
            TrueTypeFont font = new(Create(glyph, Triangle()));
            if (font.Outline(0).CommandCount != 8)
            {
                throw new InvalidDataException("Composite instructions changed the unhinted contour geometry.");
            }
            for (int length = 22; length < glyph.Length; length++)
            {
                Reject(new(Create(glyph[..length], Triangle())), 0);
            }
        }

        private static void Reject(TrueTypeFont font, int glyph)
        {
            try
            {
                _ = font.Outline(glyph);
            }
            catch (InvalidDataException)
            {
                return;
            }
            throw new InvalidDataException("An outline consumed bytes outside its own glyph record.");
        }

        private static byte[] Triangle()
        {
            byte[] glyph = new byte[20];
            BinaryPrimitives.WriteInt16BigEndian(glyph, 1);
            BinaryPrimitives.WriteInt16BigEndian(glyph.AsSpan(6), 100);
            BinaryPrimitives.WriteInt16BigEndian(glyph.AsSpan(8), 100);
            BinaryPrimitives.WriteUInt16BigEndian(glyph.AsSpan(10), 2);
            glyph[14] = 0x31;
            glyph[15] = 0x33;
            glyph[16] = 0x27;
            glyph[17] = 100;
            glyph[18] = 100;
            glyph[19] = 100;
            return glyph;
        }

        private static byte[] Composite()
        {
            byte[] glyph = new byte[16];
            BinaryPrimitives.WriteInt16BigEndian(glyph, -1);
            BinaryPrimitives.WriteUInt16BigEndian(glyph.AsSpan(10), 2);
            BinaryPrimitives.WriteUInt16BigEndian(glyph.AsSpan(12), 1);
            glyph[14] = 20;
            glyph[15] = 30;
            return glyph;
        }

        internal static byte[] Create(params byte[][] glyphs)
        {
            byte[] head = new byte[54];
            BinaryPrimitives.WriteUInt32BigEndian(head, 0x10000);
            BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5f0f3cf5);
            BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);
            BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(50), 1);
            byte[] hhea = new byte[36];
            BinaryPrimitives.WriteUInt32BigEndian(hhea, 0x10000);
            BinaryPrimitives.WriteUInt16BigEndian(hhea.AsSpan(34), 1);
            byte[] hmtx = new byte[4 + (glyphs.Length - 1) * 2];
            BinaryPrimitives.WriteUInt16BigEndian(hmtx, 1000);
            byte[] maxp = new byte[32];
            BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x10000);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), checked((ushort)glyphs.Length));
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(6), 3);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(10), 3);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(14), 1);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(28), 1);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(30), 1);
            byte[] cmap = new byte[274];
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
            BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(14), 262);
            byte[] loca = new byte[(glyphs.Length + 1) * 4];
            byte[] glyf = new byte[glyphs.Sum(value => value.Length)];
            int position = 0;
            for (int index = 0; index < glyphs.Length; index++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(index * 4), (uint)position);
                glyphs[index].CopyTo(glyf, position);
                position += glyphs[index].Length;
            }
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(glyphs.Length * 4), (uint)position);
            (string Name, byte[] Data)[] tables = [("cmap", cmap), ("glyf", glyf), ("head", head),
                ("hhea", hhea), ("hmtx", hmtx), ("loca", loca), ("maxp", maxp)];
            int offset = 12 + tables.Length * 16;
            byte[] output = new byte[offset + tables.Sum(table => (table.Data.Length + 3) & ~3)];
            BinaryPrimitives.WriteUInt32BigEndian(output, 0x10000);
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), (ushort)tables.Length);
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6), 64);
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(8), 2);
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(10), 48);
            for (int index = 0; index < tables.Length; index++)
            {
                (string name, byte[] data) = tables[index];
                Span<byte> record = output.AsSpan(12 + index * 16, 16);
                Encoding.ASCII.GetBytes(name, record);
                BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
                BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)data.Length);
                data.CopyTo(output, offset);
                offset += (data.Length + 3) & ~3;
            }
            return output;
        }
    }
}
