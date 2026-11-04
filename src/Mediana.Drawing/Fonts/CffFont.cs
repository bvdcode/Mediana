// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Mediana.Drawing.Fonts
{
    internal class CffFont
    {
        private readonly ReadOnlyMemory<byte>[] _glyphs;
        private readonly ReadOnlyMemory<byte>[] _global;
        private readonly ReadOnlyMemory<byte>[] _local;
        private readonly Matrix3x2 _matrix;

        internal CffFont(ReadOnlyMemory<byte> data, int glyphCount, float units)
        {
            CffData.Check(data.Length, 0, 4);
            if (data.Span[0] != 1 || data.Span[2] < 4) { throw new NotSupportedException("CFF requires a version 1 header."); }
            int position = data.Span[2];
            ReadOnlyMemory<byte>[] names = CffData.Index(data, ref position), top = CffData.Index(data, ref position);
            if (names.Length != 1 || top.Length != 1) { throw new InvalidDataException("OpenType CFF requires one font."); }
            _ = CffData.Index(data, ref position);
            _global = CffData.Index(data, ref position);
            Dictionary<int, double[]> dictionary = CffData.Dictionary(top[0].Span);
            if (dictionary.ContainsKey(1230)) { throw new NotSupportedException("CID-keyed CFF outlines are not implemented."); }
            if (dictionary.TryGetValue(1206, out double[]? type) && (type.Length != 1 || type[0] != 2))
            { throw new NotSupportedException("CFF requires Type 2 charstrings."); }
            if (!dictionary.TryGetValue(17, out double[]? offset) || offset.Length != 1) { throw new InvalidDataException("Missing CFF CharStrings offset."); }
            position = CffData.Integer(offset[0]);
            _glyphs = CffData.Index(data, ref position);
            if (_glyphs.Length != glyphCount) { throw new InvalidDataException("CFF CharStrings count differs from maxp."); }
            _local = [];
            if (dictionary.TryGetValue(18, out double[]? declaration))
            {
                if (declaration.Length != 2) { throw new InvalidDataException("Invalid CFF Private DICT declaration."); }
                int length = CffData.Integer(declaration[0]), start = CffData.Integer(declaration[1]);
                CffData.Check(data.Length, start, length);
                Dictionary<int, double[]> settings = CffData.Dictionary(data.Span.Slice(start, length));
                if (settings.TryGetValue(19, out double[]? subrs))
                {
                    if (subrs.Length != 1) { throw new InvalidDataException("Invalid CFF local subroutine offset."); }
                    position = checked(start + CffData.Integer(subrs[0]));
                    _local = CffData.Index(data, ref position);
                }
            }
            double[] matrix = dictionary.GetValueOrDefault(1207) ?? [0.001, 0, 0, 0.001, 0, 0];
            if (matrix.Length != 6) { throw new InvalidDataException("Invalid CFF FontMatrix."); }
            float F(int i)
            {
                double value = matrix[i] * units;
                if (!double.IsFinite(value) || Math.Abs(value) > float.MaxValue) { throw new InvalidDataException("Invalid CFF FontMatrix value."); }
                return (float)value;
            }
            _matrix = new(F(0), F(1), F(2), F(3), F(4), F(5));
        }

        internal VectorPath Outline(int glyph)
        {
            if ((uint)glyph >= _glyphs.Length) { throw new InvalidDataException("Invalid CFF glyph index."); }
            return new Type2Outline(_local, _global).Read(_glyphs[glyph]).Transform(_matrix);
        }
    }
}
