// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Drawing
{
    internal static partial class PixelCompositor
    {
        internal static void BlendCoverageSpan(Span<byte> pixels, ReadOnlySpan<float> coverage, Rgba32 source)
        {
            if (source.A == 0) { return; }
            int offset = 0;
            if (BitConverter.IsLittleEndian && Vector.IsHardwareAccelerated)
            {
                Span<Vector<uint>> destination = MemoryMarshal.Cast<byte, Vector<uint>>(pixels);
                ReadOnlySpan<Vector<float>> masks = MemoryMarshal.Cast<float, Vector<float>>(coverage);
                Vector<float> opacity = new(source.A);
                for (int i = 0; i < destination.Length; i++)
                {
                    Vector<float> shape = Vector.Min(Vector<float>.One, Vector.Max(Vector<float>.Zero, masks[i]));
                    Vector<uint> alpha = Vector.ConvertToUInt32(Vector.Round(opacity * shape));
                    if (Vector.EqualsAll(alpha, Vector<uint>.Zero)) { continue; }
                    destination[i] = BlendNormalVector(destination[i], source, alpha);
                }
                offset = destination.Length * Vector<uint>.Count;
            }
            for (; offset < coverage.Length; offset++)
            {
                byte alpha = (byte)MathF.Round(source.A * Math.Clamp(coverage[offset], 0, 1));
                Blend(pixels, offset * 4, source with { A = alpha }, ColorBlendMode.Normal);
            }
        }

        private static Vector<uint> BlendNormalVector(Vector<uint> backdrop, Rgba32 source, Vector<uint> sourceBytes)
        {
            Vector<uint> packed = new((uint)source.R | ((uint)source.G << 8) | ((uint)source.B << 16));
            packed |= sourceBytes << 24;
            if (Vector.EqualsAll(sourceBytes, new Vector<uint>(255))) { return packed; }
            Vector<uint> mask = new(255), backdropBytes = backdrop >> 24;
            if (Vector.EqualsAll(backdropBytes, Vector<uint>.Zero))
            {
                return Vector.ConditionalSelect(Vector.Equals(sourceBytes, Vector<uint>.Zero), backdrop, packed);
            }
            if (Vector.EqualsAll(backdropBytes, mask))
            {
                Vector<uint> r = BlendOpaqueChannel(source.R, backdrop & mask, sourceBytes);
                Vector<uint> g = BlendOpaqueChannel(source.G, (backdrop >> 8) & mask, sourceBytes);
                Vector<uint> b = BlendOpaqueChannel(source.B, (backdrop >> 16) & mask, sourceBytes);
                return r | (g << 8) | (b << 16) | new Vector<uint>(0xff000000);
            }
            Vector<float> sourceAlpha = Vector.ConvertToSingle(sourceBytes) / new Vector<float>(255);
            Vector<float> retained = Vector<float>.One - sourceAlpha;
            Vector<float> backdropAlpha = Vector.ConvertToSingle(backdropBytes) / new Vector<float>(255);
            Vector<float> alpha = sourceAlpha + backdropAlpha * retained;
            Vector<uint> red = BlendChannelVector(source.R, backdrop & mask, sourceAlpha, retained, backdropAlpha, alpha);
            Vector<uint> green = BlendChannelVector(source.G, (backdrop >> 8) & mask, sourceAlpha, retained, backdropAlpha, alpha);
            Vector<uint> blue = BlendChannelVector(source.B, (backdrop >> 16) & mask, sourceAlpha, retained, backdropAlpha, alpha);
            Vector<uint> blended = red | (green << 8) | (blue << 16) | (Vector.ConvertToUInt32(Vector.Round(alpha * new Vector<float>(255))) << 24);
            blended = Vector.ConditionalSelect(Vector.Equals(backdropBytes, Vector<uint>.Zero) | Vector.Equals(sourceBytes, mask), packed, blended);
            return Vector.ConditionalSelect(Vector.Equals(sourceBytes, Vector<uint>.Zero), backdrop, blended);
        }

        private static Vector<uint> BlendOpaqueChannel(byte source, Vector<uint> backdrop, Vector<uint> alpha)
        {
            Vector<uint> total = new Vector<uint>(source) * alpha + backdrop * (new Vector<uint>(255) - alpha) + new Vector<uint>(127);
            return (total + Vector<uint>.One + (total >> 8)) >> 8;
        }
    }
}
