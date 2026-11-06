// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;

namespace Mediana.Drawing
{
    internal static partial class PixelCompositor
    {
        internal static void BlendSpan(Span<byte> pixels, Rgba32 source)
        {
            if (source.A == 0) { return; }
            if (source.A == 255) { Fill(pixels, source); return; }
            int offset = 0;
            if (BitConverter.IsLittleEndian && Vector.IsHardwareAccelerated)
            {
                Span<Vector<uint>> vectors = MemoryMarshal.Cast<byte, Vector<uint>>(pixels);
                Vector<uint> mask = new(255), packed = new((uint)source.R | ((uint)source.G << 8) | ((uint)source.B << 16) | ((uint)source.A << 24));
                Vector<float> sourceAlpha = new(source.A / 255f), retained = Vector<float>.One - sourceAlpha;
                for (int i = 0; i < vectors.Length; i++)
                {
                    Vector<uint> backdrop = vectors[i];
                    Vector<uint> a = backdrop >> 24;
                    if (Vector.EqualsAll(a, Vector<uint>.Zero)) { vectors[i] = packed; continue; }
                    Vector<float> ba = Vector.ConvertToSingle(a) / new Vector<float>(255);
                    Vector<float> alpha = sourceAlpha + ba * retained;
                    Vector<uint> r = BlendChannelVector(source.R, backdrop & mask, sourceAlpha, retained, ba, alpha);
                    Vector<uint> g = BlendChannelVector(source.G, (backdrop >> 8) & mask, sourceAlpha, retained, ba, alpha);
                    Vector<uint> b = BlendChannelVector(source.B, (backdrop >> 16) & mask, sourceAlpha, retained, ba, alpha);
                    Vector<uint> blended = r | (g << 8) | (b << 16) | (Vector.ConvertToUInt32(Vector.Round(alpha * new Vector<float>(255))) << 24);
                    vectors[i] = Vector.ConditionalSelect(Vector.Equals(a, Vector<uint>.Zero), packed, blended);
                }
                offset = vectors.Length * Vector<byte>.Count;
            }
            for (; offset < pixels.Length; offset += 4) { Blend(pixels, offset, source, ColorBlendMode.Normal); }
        }

        private static Vector<uint> BlendChannelVector(byte source, Vector<uint> backdrop, Vector<float> sourceAlpha,
            Vector<float> retained, Vector<float> backdropAlpha, Vector<float> alpha)
        {
            Vector<float> color = new(source / 255f);
            Vector<float> mixed = (Vector<float>.One - backdropAlpha) * color + backdropAlpha * color;
            Vector<float> result = (sourceAlpha * mixed + retained * backdropAlpha * (Vector.ConvertToSingle(backdrop) / new Vector<float>(255))) / alpha;
            return Vector.ConvertToUInt32(Vector.Round(result * new Vector<float>(255)));
        }
    }
}
