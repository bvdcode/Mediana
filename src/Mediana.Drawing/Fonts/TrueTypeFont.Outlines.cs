// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Mediana.Drawing.Fonts
{
    public partial class TrueTypeFont
    {


        private GlyphShape ReadGlyph(int glyph, HashSet<int> active, int depth)
        {
            if ((uint)glyph >= _glyphs || depth >= 32 || !active.Add(glyph))
            {
                throw new InvalidDataException("Invalid or cyclic TrueType composite glyph.");
            }

            try
            {
                int table = Table("loca", 0), unit = _locaFormat == 0 ? 2 : 4;
                int start = _locaFormat == 0 ? U16(table + (glyph * unit)) * 2 : Offset(table + (glyph * unit));
                int end = _locaFormat == 0 ? U16(table + ((glyph + 1) * unit)) * 2 : Offset(table + ((glyph + 1) * unit));
                (int Start, int Length) data = _tables["glyf"];
                if (end < start || end > data.Length)
                {
                    throw new InvalidDataException("Invalid TrueType glyph location.");
                }

                if (start == end)
                {
                    return new GlyphShape(new List<FontPoint>(), new List<int>());
                }

                FontGlyphReader reader = new(_data.AsSpan(data.Start + start, end - start));
                int contours = reader.ReadInt16();
                reader.Skip(8);
                return contours >= 0 ? Simple(contours, ref reader) : Composite(ref reader, active, depth);
            }
            finally
            {
                active.Remove(glyph);
            }
        }

        private GlyphShape Simple(int contours, ref FontGlyphReader reader)
        {
            if (contours == 0 && reader.IsEmpty)
            {
                return new([], []);
            }
            FontGlyphReader endpoints = new(reader.Read(checked(contours * sizeof(ushort))));
            int instructions = reader.ReadUInt16();
            reader.Skip(instructions);
            var ends = new List<int>(contours);
            for (int i = 0; i < contours; i++)
            {
                int end = endpoints.ReadUInt16();
                if (i > 0 && end <= ends[i - 1])
                {
                    throw new InvalidDataException("Invalid TrueType contour endpoints.");
                }

                ends.Add(end);
            }

            int count = contours == 0 ? 0 : ends[^1] + 1;
            if (count > 0)
            {
                reader.Require(1);
            }
            byte[] flags = new byte[count];
            for (int i = 0; i < count; i++)
            {
                byte flag = reader.ReadByte();
                flags[i] = flag;
                if ((flag & 8) != 0)
                {
                    int repeat = reader.ReadByte();
                    if (repeat > count - i - 1)
                    {
                        throw new InvalidDataException("Invalid repeated TrueType flags.");
                    }

                    while (repeat-- > 0)
                    {
                        flags[++i] = flag;
                    }
                }
            }

            var points = new List<FontPoint>(count);
            int x = 0;
            for (int i = 0; i < count; i++)
            {
                x = checked(x + Coordinate(flags[i], 2, 16, ref reader));
                points.Add(new FontPoint(new Vector2(x, 0), (flags[i] & 1) != 0));
            }

            int y = 0;
            for (int i = 0; i < count; i++)
            {
                y = checked(y + Coordinate(flags[i], 4, 32, ref reader));
                points[i] = points[i] with
                {
                    Position = new Vector2(points[i].Position.X, y)
                };
            }

            return new GlyphShape(points, ends);
        }

        private static int Coordinate(byte flags, int shortMask, int positiveMask, ref FontGlyphReader reader)
        {
            if ((flags & shortMask) != 0)
            {
                int value = reader.ReadByte();
                return (flags & positiveMask) != 0 ? value : -value;
            }

            if ((flags & positiveMask) != 0)
            {
                return 0;
            }

            return reader.ReadInt16();
        }

        private GlyphShape Composite(ref FontGlyphReader reader, HashSet<int> active, int depth)
        {
            GlyphShape? result = new GlyphShape(new List<FontPoint>(), new List<int>());
            int flags;
            bool instructions = false;
            do
            {
                flags = reader.ReadUInt16();
                instructions |= (flags & 256) != 0;
                int glyph = reader.ReadUInt16();
                bool xy = (flags & 2) != 0;
                int a = CompositeArgument(flags, ref reader), b = CompositeArgument(flags, ref reader);
                Matrix3x2 transform = CompositeTransform(flags, ref reader);
                GlyphShape component = ReadGlyph(glyph, active, depth + 1);
                Vector2 offset;
                if (xy)
                {
                    offset = new Vector2(a, b);
                    if ((flags & 2048) != 0)
                    {
                        offset = Vector2.TransformNormal(offset, transform);
                    }

                    if ((flags & 4) != 0)
                    {
                        offset = new Vector2(MathF.Round(offset.X), MathF.Round(offset.Y));
                    }
                }
                else
                {
                    if ((uint)a >= result.Points.Count || (uint)b >= component.Points.Count)
                    {
                        throw new InvalidDataException("Invalid composite glyph attachment points.");
                    }

                    offset = result.Points[a].Position - Vector2.Transform(component.Points[b].Position, transform);
                }

                int previous = result.Points.Count;
                if (previous + component.Points.Count > 100_000)
                {
                    throw new InvalidDataException("TrueType composite exceeds 100,000 points.");
                }

                foreach (FontPoint point in component.Points)
                {
                    result.Points.Add(point with { Position = Vector2.Transform(point.Position, transform) + offset });
                }

                foreach (int end in component.Ends)
                {
                    result.Ends.Add(previous + end);
                }
            }
            while ((flags & 32) != 0);
            if (instructions)
            {
                reader.Skip(reader.ReadUInt16());
            }

            return result;
        }

        private static int CompositeArgument(int flags, ref FontGlyphReader reader)
        {
            bool xy = (flags & 2) != 0, words = (flags & 1) != 0;
            return words ? xy ? reader.ReadInt16() : reader.ReadUInt16() : xy ? (sbyte)reader.ReadByte() : reader.ReadByte();
        }

        private static Matrix3x2 CompositeTransform(int flags, ref FontGlyphReader reader)
        {
            Matrix3x2 transform = Matrix3x2.Identity;
            if ((flags & 8) != 0)
            {
                transform = Matrix3x2.CreateScale(reader.ReadInt16() / 16384f);
            }
            else if ((flags & 64) != 0)
            {
                transform = Matrix3x2.CreateScale(reader.ReadInt16() / 16384f, reader.ReadInt16() / 16384f);
            }
            else if ((flags & 128) != 0)
            {
                transform = new Matrix3x2(reader.ReadInt16() / 16384f, reader.ReadInt16() / 16384f, reader.ReadInt16() / 16384f, reader.ReadInt16() / 16384f, 0, 0);
            }

            return transform;
        }

        private static VectorPath Path(GlyphShape shape)
        {
            VectorPath? path = new VectorPath();
            int first = 0;
            foreach (int end in shape.Ends)
            {
                FontPoint a = shape.Points[first], z = shape.Points[end];
                Vector2 start = a.OnCurve ? a.Position : z.OnCurve ? z.Position : (a.Position + z.Position) / 2;
                path.MoveTo(start.X, start.Y);
                int position = a.OnCurve ? first + 1 : first, stop = !a.OnCurve && z.OnCurve ? end : end + 1;
                while (position < stop)
                {
                    FontPoint point = shape.Points[position];
                    if (point.OnCurve)
                    {
                        path.LineTo(point.Position.X, point.Position.Y);
                        position++;
                    }
                    else
                    {
                        FontPoint next = position + 1 < stop ? shape.Points[position + 1] : new FontPoint(start, true);
                        Vector2 target = next.OnCurve ? next.Position : (point.Position + next.Position) / 2;
                        path.QuadraticTo(point.Position.X, point.Position.Y, target.X, target.Y);
                        position += next.OnCurve ? 2 : 1;
                    }
                }

                path.Close();
                first = end + 1;
            }

            return path;
        }
    }
}
