// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Processing
{
    internal class IccMultiStageLutReader(ReadOnlyMemory<byte> tag)
    {
        private readonly List<(int Offset, int Length)> extents = [];

        internal static IccCmykLut Read(ReadOnlyMemory<byte> tag, bool lab, CancellationToken token) => new IccMultiStageLutReader(tag).Read(lab, token);

        private IccCmykLut Read(bool lab, CancellationToken token)
        {
            ReadOnlySpan<byte> bytes = tag.Span;
            if (bytes.Length < 32 || bytes[10] != 0 || bytes[11] != 0)
            { throw new InvalidDataException("Invalid ICC multi-stage LUT header."); }
            int b = Offset(12), matrix = Offset(16), m = Offset(20), clut = Offset(24), a = Offset(28);
            if (a == 0 || b == 0 || clut == 0 || (matrix == 0) != (m == 0))
            { throw new InvalidDataException("ICC CMYK multi-stage LUT requires A, CLUT and B, with paired M and matrix stages."); }
            IccToneCurve[] inputs = Curves(a, 4, token), outputs = Curves(b, 3, token);
            IccToneCurve[]? middle = m == 0 ? null : Curves(m, 3, token);
            RgbMatrix transform = RgbMatrix.Identity;
            Vector3 translation = Vector3.Zero;
            if (matrix != 0)
            {
                ReadOnlyMemory<byte> data = Part(matrix, 48);
                double V(int i) => IccToneCurve.Fixed(data.Span[(i * 4)..]);
                transform = new(V(0), V(1), V(2), V(3), V(4), V(5), V(6), V(7), V(8));
                translation = new((float)V(9), (float)V(10), (float)V(11));
            }
            if (tag.Length - clut < 20) { throw new InvalidDataException("Truncated ICC CLUT header."); }
            ReadOnlySpan<byte> header = bytes.Slice(clut, 20);
            int precision = header[16];
            if (precision is not (1 or 2) || header.Slice(4, 12).ContainsAnyExcept((byte)0) || header[17..].ContainsAnyExcept((byte)0))
            { throw new InvalidDataException("Invalid ICC CLUT precision or reserved fields."); }
            long cells = 1;
            for (int i = 0; i < 4; i++)
            {
                if (header[i] < 2) { throw new InvalidDataException("Invalid ICC CLUT grid dimension."); }
                cells *= header[i];
            }
            long length = 20 + cells * 3 * precision;
            if (length > tag.Length - clut) { throw new InvalidDataException("ICC CLUT exceeds its tag."); }
            ReadOnlyMemory<byte> values = Part(clut, (int)length)[20..];
            ValidateExtents();
            IccClut4 table = new(values, header[..4], precision, inputs, token);
            IccPcsEncoding encoding = lab ? IccPcsEncoding.Lab : IccPcsEncoding.Xyz;
            return new(table, outputs, middle, transform, translation, encoding);
        }

        private int Offset(int position)
        {
            uint offset = IccProfile.U32(tag.Span[position..]);
            if (offset == 0) { return 0; }
            if (offset < 32 || offset > tag.Length || (offset & 3) != 0)
            { throw new InvalidDataException("ICC processing-element offset is invalid."); }
            return (int)offset;
        }

        private ReadOnlyMemory<byte> Part(int position, int count)
        {
            if (count < 0 || count > tag.Length - position) { throw new InvalidDataException("ICC processing element exceeds its tag."); }
            extents.Add((position, count));
            return tag.Slice(position, count);
        }

        private IccToneCurve[] Curves(int position, int count, CancellationToken token)
        {
            IccToneCurve[] curves = new IccToneCurve[count];
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                if (position > tag.Length) { throw new InvalidDataException("ICC curve group exceeds its tag."); }
                curves[i] = new(tag[position..]);
                Part(position, curves[i].EncodedLength);
                position += curves[i].EncodedLength;
                if (i + 1 < count)
                {
                    int padding = -position & 3;
                    if (padding > tag.Length - position || tag.Span.Slice(position, padding).ContainsAnyExcept((byte)0))
                    { throw new InvalidDataException("ICC curve padding is truncated or nonzero."); }
                    position += padding;
                }
            }
            return curves;
        }

        private void ValidateExtents()
        {
            extents.Sort((left, right) => left.Offset.CompareTo(right.Offset));
            for (int i = 1; i < extents.Count; i++)
            {
                (int Offset, int Length) previous = extents[i - 1];
                if (extents[i].Offset < previous.Offset + previous.Length && extents[i] != previous)
                { throw new InvalidDataException("ICC processing elements overlap without sharing an entire curve."); }
            }
        }
    }
}
