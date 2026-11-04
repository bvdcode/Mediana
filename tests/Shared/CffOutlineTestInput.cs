// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Drawing.Fonts;
using System.Buffers.Binary;
using System.Numerics;

namespace Mediana.Tests
{
    internal static class CffOutlineTestInput
    {
        internal static byte[] Rectangle => [139, 139, 21, 239, 139, 139, 239, 39, 139, 5, 14];

        internal static void VerifyPaths()
        {
            VectorPath path = new Type2Outline([], []).Read(Rectangle);
            Require(path.CommandCount == 5 && path.Elements[1].First == new System.Numerics.Vector2(100, 0)
                && path.Elements[2].First == new System.Numerics.Vector2(100, 100)
                && path.Elements[3].First == new System.Numerics.Vector2(0, 100));
            VectorPath curve = new Type2Outline([], []).Read(new byte[] { 149, 159, 21, 169, 179, 189, 199, 209, 219, 8, 14 });
            Require(curve.CommandCount == 3 && curve.Elements[1].First == new System.Numerics.Vector2(40, 60)
                && curve.Elements[1].Second == new System.Numerics.Vector2(90, 120)
                && curve.Elements[1].Third == new System.Numerics.Vector2(160, 200));
            ReadOnlyMemory<byte>[] local = [new byte[] { 239, 139, 5, 11 }];
            ReadOnlyMemory<byte>[] global = [new byte[] { 139, 239, 5, 11 }];
            VectorPath subrs = new Type2Outline(local, global).Read(new byte[] { 248, 136, 139, 159, 1, 19, 128, 139, 139, 21, 32, 10, 32, 29, 14 });
            Require(subrs.CommandCount == 4 && subrs.Elements[1].First == new System.Numerics.Vector2(100, 0)
                && subrs.Elements[2].First == new System.Numerics.Vector2(100, 100));
            CffFont font = new(Table(Rectangle), 1, 1000);
            Require(font.Outline(0).Elements[2].First == new System.Numerics.Vector2(100, 100));
            byte[] sfnt = FontGlyphTestInput.Create(Table(Rectangle));
            "OTTO"u8.CopyTo(sfnt);
            int tables = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
            for (int i = 0; i < tables; i++)
            {
                Span<byte> record = sfnt.AsSpan(12 + i * 16, 16);
                if (record[..4].SequenceEqual("glyf"u8)) { "CFF "u8.CopyTo(record); }
            }
            TrueTypeFont openType = new(sfnt);
            VectorPath outline = openType.Outline(0);
            Require(outline.CommandCount == 5 && outline.Elements[2].First == new Vector2(100, 100));
            outline.LineTo(999, 999);
            Require(openType.Outline(0).CommandCount == 5 && openType.Advance(0) == 1000);
            VerifyCurve(26, [10, 20, 30, 40], new(20, 80));
            VerifyCurve(27, [10, 20, 30, 40], new(70, 30));
            VerifyCurve(26, [5, 10, 20, 30, 40], new(25, 80));
            VerifyCurve(27, [5, 10, 20, 30, 40], new(70, 35));
            VerifyCurve(30, [10, 20, 30, 40], new(60, 40));
            VerifyCurve(31, [10, 20, 30, 40], new(30, 70));
            VerifyCurve(30, [10, 20, 30, 40, 5], new(60, 45));
            VerifyCurve(31, [10, 20, 30, 40, 5], new(35, 70));
            VerifyCurve(1234, [10, 20, 30, 40, 50, 60, 70], new(250, 0), 2);
            VerifyCurve(1235, [10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 50], new(60, 60), 2);
            VerifyCurve(1236, [10, 5, 20, 10, 30, 40, 50, 15, 60], new(210, 0), 2);
            VerifyCurve(1237, [10, 5, 10, 5, 10, 5, 10, 5, 10, 5, 20], new(70, 0), 2);
            VerifyCurve(1237, [5, 10, 5, 10, 5, 10, 5, 10, 5, 10, 20], new(0, 70), 2);
        }

        internal static void VerifyRejected()
        {
            byte[] program = Rectangle;
            for (int length = 0; length < program.Length; length++)
            {
                int count = length;
                Reject(() => new Type2Outline([], []).Read(program.AsMemory(0, count)));
            }
            Reject(() => new Type2Outline([], []).Read(new byte[] { 11 }));
            Reject(() => new Type2Outline([], []).Read(new byte[] { 139, 1, 19 }));
            Reject(() => new Type2Outline([], []).Read(new byte[] { 139, 139, 5, 14 }));
            byte[] overflow = Enumerable.Repeat((byte)139, 49).Append((byte)14).ToArray();
            Reject(() => new Type2Outline([], []).Read(overflow));
            ReadOnlyMemory<byte>[] recursive = [new byte[] { 32, 10, 11 }];
            Reject(() => new Type2Outline(recursive, []).Read(new byte[] { 32, 10, 14 }));
            byte[] table = Table(Rectangle);
            for (int length = 0; length < table.Length; length++)
            {
                int count = length;
                Reject(() => new CffFont(table.AsMemory(0, count), 1, 1000));
            }
            Reject(() => new CffFont(table, 2, 1000));
            byte[] reversed = table.ToArray(); reversed[8] = 0;
            Reject(() => new CffFont(reversed, 1, 1000));
        }

        internal static byte[] Table(byte[] glyph) =>
            [1, 0, 4, 4, 0, 1, 1, 1, 2, (byte)'P', 0, 1, 1, 1, 3, 160, 17, 0, 0, 0, 0,
             0, 1, 1, 1, checked((byte)(glyph.Length + 1)), .. glyph];

        private static void VerifyCurve(int code, int[] values, Vector2 end, int curves = 1)
        {
            List<byte> program = [139, 139, 21];
            program.AddRange(values.Select(value => checked((byte)(value + 139))));
            if (code >= 1200) { program.Add(12); program.Add((byte)(code - 1200)); }
            else { program.Add((byte)code); }
            program.Add(14);
            VectorPath path = new Type2Outline([], []).Read(program.ToArray());
            Require(path.CommandCount == curves + 2 && path.Elements[^2].Third == end);
        }

        private static void Reject(Action action)
        {
            try { action(); }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException) { return; }
            throw new InvalidDataException("CFF parsing accepted malformed data or an excessive program.");
        }
        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("CFF contours, curve controls, widths or subroutine state differ."); }
        }
    }
}
