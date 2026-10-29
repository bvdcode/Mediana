// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Converts nonlinear YCbCr to RGB with Q20 coefficients.
    /// </summary>
    /// <remarks>
    /// Limited range uses Y 16..235 and C 16..240 scaled by 2^(depth-8). Full range
    /// uses 0..2^depth-1 with chroma centred at 2^(depth-1). Each coefficient is rounded
    /// once to Q20; each final channel rounds by adding 2^19 then shifting and clipping.
    /// This operation does not change the transfer function or colour primaries.
    /// </remarks>
    internal class ColorConversion
    {
        private const int FractionBits = 20;
        private const long Scale = 1L << FractionBits;
        private readonly int yOffset;
        private readonly int chromaOffset;
        private readonly long yFactor;
        private readonly long redCr;
        private readonly long blueCb;
        private readonly long greenCb;
        private readonly long greenCr;

        public ColorConversion(int depth, bool fullRange, int matrix)
        {
            if ((depth != 8) && (depth != 10))
            {
                throw new ArgumentOutOfRangeException(nameof(depth));
            }
            double kr;
            double kb;
            switch (matrix)
            {
                case 1:
                    kr = 0.2126;
                    kb = 0.0722;
                    break;
                case 2:
                case 5:
                case 6:
                    kr = 0.299;
                    kb = 0.114;
                    break;
                case 9:
                    kr = 0.2627;
                    kb = 0.0593;
                    break;
                default:
                    throw new UnsupportedHeicFeatureException("YCbCr matrix coefficients " + matrix);
            }

            int unit = 1 << (depth - 8);
            int maximum = (1 << depth) - 1;
            int yRange = fullRange ? maximum : 219 * unit;
            int cRange = fullRange ? maximum : 224 * unit;
            yOffset = fullRange ? 0 : 16 * unit;
            chromaOffset = 1 << (depth - 1);
            yFactor = Fixed(255.0 / yRange);
            redCr = Fixed((255.0 / cRange) * 2 * (1 - kr));
            blueCb = Fixed((255.0 / cRange) * 2 * (1 - kb));
            greenCb = Fixed(((-255.0 / cRange) * 2 * kb * (1 - kb)) / (1 - kr - kb));
            greenCr = Fixed(((-255.0 / cRange) * 2 * kr * (1 - kr)) / (1 - kr - kb));
        }

        public void ConvertRows(ReadOnlySpan<ushort> luma, ReadOnlySpan<ushort> cb, ReadOnlySpan<ushort> cr,
            ColorPlaneLayout layout, Span<byte> rgba)
        {
            if ((luma.Length < layout.LumaLength) || (cb.Length < layout.ChromaLength) ||
                (cr.Length < layout.ChromaLength) || (rgba.Length < layout.OutputLength))
            {
                throw new ArgumentException("Colour plane buffers are shorter than the visible image.");
            }
            int bytes = layout.Width * 4;
            int subWidth = layout.SubWidth;
            ColorConversionVectors vectors = new ColorConversionVectors(yOffset, chromaOffset, yFactor, redCr, blueCb, greenCb, greenCr);
            for (int row = 0; row < layout.Height; row++)
            {
                ReadOnlySpan<ushort> ySamples = luma.Slice(row * layout.LumaStride, layout.Width);
                int chromaOffset = ((row + layout.ChromaPhaseY) / layout.SubHeight) * layout.ChromaStride;
                ReadOnlySpan<ushort> cbSamples = cb.Slice(chromaOffset, layout.ChromaWidth);
                ReadOnlySpan<ushort> crSamples = cr.Slice(chromaOffset, layout.ChromaWidth);
                Span<byte> destination = rgba.Slice(row * bytes, bytes);
                int x = 0;
                if ((layout.ChromaPhaseX != 0) && (layout.Width != 0))
                {
                    Convert(ySamples[0], cbSamples[0], crSamples[0], destination.Slice(0, 4));
                    x = 1;
                }
                if (Vector.IsHardwareAccelerated)
                {
                    int chromaStart = (x + layout.ChromaPhaseX) / subWidth;
                    ReadOnlySpan<Vector<ushort>> yRows = MemoryMarshal.Cast<ushort, Vector<ushort>>(ySamples.Slice(x));
                    ReadOnlySpan<Vector<ushort>> cbRows = MemoryMarshal.Cast<ushort, Vector<ushort>>(cbSamples.Slice(chromaStart));
                    ReadOnlySpan<Vector<ushort>> crRows = MemoryMarshal.Cast<ushort, Vector<ushort>>(crSamples.Slice(chromaStart));
                    Span<Vector<int>> output = MemoryMarshal.Cast<byte, Vector<int>>(destination.Slice(x * 4));
                    int count = (layout.Width - x) / (Vector<ushort>.Count * subWidth);
                    if (subWidth == 1)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            ConvertSamples(in vectors, yRows[i], cbRows[i], crRows[i], output.Slice(i * 2, 2));
                        }
                    }
                    else
                    {
                        Vector<uint> duplicate = new Vector<uint>(0x00010001);
                        for (int i = 0; i < count; i++)
                        {
                            Vector.Widen(cbRows[i], out Vector<uint> cbFirst, out Vector<uint> cbSecond);
                            Vector.Widen(crRows[i], out Vector<uint> crFirst, out Vector<uint> crSecond);
                            ConvertSamples(in vectors, yRows[i * 2], Vector.AsVectorUInt16(cbFirst * duplicate),
                                Vector.AsVectorUInt16(crFirst * duplicate), output.Slice(i * 4, 2));
                            ConvertSamples(in vectors, yRows[(i * 2) + 1], Vector.AsVectorUInt16(cbSecond * duplicate),
                                Vector.AsVectorUInt16(crSecond * duplicate), output.Slice((i * 4) + 2, 2));
                        }
                    }
                    x += count * Vector<ushort>.Count * subWidth;
                }
                for (; x < layout.Width; x++)
                {
                    int chroma = (x + layout.ChromaPhaseX) / subWidth;
                    Convert(ySamples[x], cbSamples[chroma], crSamples[chroma], destination.Slice(x * 4, 4));
                }
            }
        }

        public void ConvertRows(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> cb, ReadOnlySpan<byte> cr,
            ColorPlaneLayout layout, Span<byte> rgba)
        {
            if ((luma.Length < layout.LumaLength) || (cb.Length < layout.ChromaLength) ||
                (cr.Length < layout.ChromaLength) || (rgba.Length < layout.OutputLength))
            {
                throw new ArgumentException("Colour plane buffers are shorter than the visible image.");
            }
            int bytes = layout.Width * 4;
            int subWidth = layout.SubWidth;
            ColorConversionVectors vectors = new ColorConversionVectors(yOffset, chromaOffset, yFactor, redCr, blueCb, greenCb, greenCr);
            for (int row = 0; row < layout.Height; row++)
            {
                ReadOnlySpan<byte> ySamples = luma.Slice(row * layout.LumaStride, layout.Width);
                int chromaOffset = ((row + layout.ChromaPhaseY) / layout.SubHeight) * layout.ChromaStride;
                ReadOnlySpan<byte> cbSamples = cb.Slice(chromaOffset, layout.ChromaWidth);
                ReadOnlySpan<byte> crSamples = cr.Slice(chromaOffset, layout.ChromaWidth);
                Span<byte> destination = rgba.Slice(row * bytes, bytes);
                int x = 0;
                if ((layout.ChromaPhaseX != 0) && (layout.Width != 0))
                {
                    Convert(ySamples[0], cbSamples[0], crSamples[0], destination.Slice(0, 4));
                    x = 1;
                }
                if (Vector.IsHardwareAccelerated)
                {
                    int chromaStart = (x + layout.ChromaPhaseX) / subWidth;
                    ReadOnlySpan<Vector<byte>> yRows = MemoryMarshal.Cast<byte, Vector<byte>>(ySamples.Slice(x));
                    ReadOnlySpan<Vector<byte>> cbRows = MemoryMarshal.Cast<byte, Vector<byte>>(cbSamples.Slice(chromaStart));
                    ReadOnlySpan<Vector<byte>> crRows = MemoryMarshal.Cast<byte, Vector<byte>>(crSamples.Slice(chromaStart));
                    Span<Vector<int>> output = MemoryMarshal.Cast<byte, Vector<int>>(destination.Slice(x * 4));
                    int count = (layout.Width - x) / (Vector<byte>.Count * subWidth);
                    for (int i = 0; i < count; i++)
                    {
                        Vector.Widen(cbRows[i], out Vector<ushort> cbFirst, out Vector<ushort> cbSecond);
                        Vector.Widen(crRows[i], out Vector<ushort> crFirst, out Vector<ushort> crSecond);
                        if (subWidth == 1)
                        {
                            Vector.Widen(yRows[i], out Vector<ushort> yFirst, out Vector<ushort> ySecond);
                            ConvertSamples(in vectors, yFirst, cbFirst, crFirst, output.Slice(i * 4, 2));
                            ConvertSamples(in vectors, ySecond, cbSecond, crSecond, output.Slice((i * 4) + 2, 2));
                        }
                        else
                        {
                            ConvertSubsampledBytes(in vectors, yRows[i * 2], cbFirst, crFirst, output.Slice(i * 8, 4));
                            ConvertSubsampledBytes(in vectors, yRows[(i * 2) + 1], cbSecond, crSecond, output.Slice((i * 8) + 4, 4));
                        }
                    }
                    x += count * Vector<byte>.Count * subWidth;
                }
                for (; x < layout.Width; x++)
                {
                    int chroma = (x + layout.ChromaPhaseX) / subWidth;
                    Convert(ySamples[x], cbSamples[chroma], crSamples[chroma], destination.Slice(x * 4, 4));
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ConvertSubsampledBytes(in ColorConversionVectors vectors, Vector<byte> y,
            Vector<ushort> cb, Vector<ushort> cr, Span<Vector<int>> output)
        {
            Vector.Widen(y, out Vector<ushort> yFirst, out Vector<ushort> ySecond);
            Vector.Widen(cb, out Vector<uint> cbFirst, out Vector<uint> cbSecond);
            Vector.Widen(cr, out Vector<uint> crFirst, out Vector<uint> crSecond);
            Vector<uint> duplicate = new Vector<uint>(0x00010001);
            ConvertSamples(in vectors, yFirst, Vector.AsVectorUInt16(cbFirst * duplicate),
                Vector.AsVectorUInt16(crFirst * duplicate), output.Slice(0, 2));
            ConvertSamples(in vectors, ySecond, Vector.AsVectorUInt16(cbSecond * duplicate),
                Vector.AsVectorUInt16(crSecond * duplicate), output.Slice(2, 2));
        }

        public void Convert(int y, int cb, int cr, Span<byte> rgba)
        {
            long luma = yFactor * (y - yOffset);
            int blue = cb - chromaOffset;
            int red = cr - chromaOffset;
            rgba[0] = Channel(luma + (redCr * red));
            rgba[1] = Channel(luma + (greenCb * blue) + (greenCr * red));
            rgba[2] = Channel(luma + (blueCb * blue));
            rgba[3] = 255;
        }

        public byte Gray(int y) => Channel(yFactor * (y - yOffset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ConvertSamples(in ColorConversionVectors vectors, Vector<ushort> y, Vector<ushort> cb,
            Vector<ushort> cr, Span<Vector<int>> output)
        {
            Vector.Widen(y, out Vector<uint> yFirst, out Vector<uint> ySecond);
            Vector.Widen(cb, out Vector<uint> cbFirst, out Vector<uint> cbSecond);
            Vector.Widen(cr, out Vector<uint> crFirst, out Vector<uint> crSecond);
            output[0] = vectors.Convert(Vector.AsVectorInt32(yFirst), Vector.AsVectorInt32(cbFirst), Vector.AsVectorInt32(crFirst));
            output[1] = vectors.Convert(Vector.AsVectorInt32(ySecond), Vector.AsVectorInt32(cbSecond), Vector.AsVectorInt32(crSecond));
        }

        private static long Fixed(double value) => (long)Math.Round(value * Scale, MidpointRounding.AwayFromZero);
        private static byte Channel(long value) => (byte)Math.Min(255L, Math.Max(0L, (value + (Scale / 2)) >> FractionBits));
    }
}
