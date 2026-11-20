// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegSampleConverter
    {
        internal static void Fill(VideoFrame input, Span<float> samples, int left, int top,
            int horizontal, int vertical, bool monochrome, JpegBackgroundColor? background)
        {
            int lumaBlocks = horizontal * vertical;
            Span<float> cb = samples.Slice(lumaBlocks * 64, 64);
            Span<float> cr = samples.Slice((lumaBlocks + 1) * 64, 64);
            cb.Clear(); cr.Clear();
            float chromaScale = 1f / lumaBlocks;
            Span<byte> padding = stackalloc byte[32];
            for (int y = 0; y < vertical * 8; y++)
            {
                ReadOnlySpan<byte> row = input.GetRow(0, Math.Min(top + y, input.Format.Height - 1));
                for (int blockX = 0; blockX < horizontal; blockX++)
                {
                    int x = left + blockX * 8;
                    Vector256<uint> rgba;
                    if (x <= input.Format.Width - 8)
                    {
                        rgba = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(row), (nuint)(x * 4)).AsUInt32();
                    }
                    else
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            row.Slice(Math.Min(x + i, input.Format.Width - 1) * 4, 4).CopyTo(padding[(i * 4)..]);
                        }
                        rgba = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(padding)).AsUInt32();
                    }
                    Vector256<float> r = Channel(rgba, 0), g = Channel(rgba, 8), b = Channel(rgba, 16);
                    Vector256<float> alpha = Channel(rgba, 24);
                    if (!Vector256.EqualsAll(alpha, Vector256.Create(255f)))
                    {
                        if (background is not JpegBackgroundColor matte)
                        {
                            throw new NotSupportedException("JPEG cannot store alpha. Composite the image or specify a JPEG background color.");
                        }
                        alpha *= 1f / 255;
                        r = Vector256.Create((float)matte.Red) + (r - Vector256.Create((float)matte.Red)) * alpha;
                        g = Vector256.Create((float)matte.Green) + (g - Vector256.Create((float)matte.Green)) * alpha;
                        b = Vector256.Create((float)matte.Blue) + (b - Vector256.Create((float)matte.Blue)) * alpha;
                    }
                    int luma = (y / 8 * horizontal + blockX) * 64 + (y & 7) * 8;
                    (r * 0.299f + g * 0.587f + b * 0.114f - Vector256.Create(128f)).StoreUnsafe(ref samples[luma]);
                    if (!monochrome)
                    {
                        Vector256<float> blue = r * -0.168735892f + g * -0.331264108f + b * 0.5f;
                        Vector256<float> red = r * 0.5f + g * -0.418687589f + b * -0.081312411f;
                        if (horizontal == 1)
                        {
                            blue.StoreUnsafe(ref cb[y * 8]);
                            red.StoreUnsafe(ref cr[y * 8]);
                        }
                        else
                        {
                            int chroma = (y >> (vertical - 1)) * 8 + blockX * 4;
                            (Vector128.LoadUnsafe(ref cb[chroma]) + Pairs(blue) * chromaScale).StoreUnsafe(ref cb[chroma]);
                            (Vector128.LoadUnsafe(ref cr[chroma]) + Pairs(red) * chromaScale).StoreUnsafe(ref cr[chroma]);
                        }
                    }
                }
            }
        }

        private static Vector128<float> Pairs(Vector256<float> values)
        {
            Vector256<float> sums = values + Vector256.Shuffle(values, Vector256.Create(1, 0, 3, 2, 5, 4, 7, 6));
            return Vector256.Shuffle(sums, Vector256.Create(0, 2, 4, 6, 0, 0, 0, 0)).GetLower();
        }

        private static Vector256<float> Channel(Vector256<uint> rgba, int littleEndianShift)
        {
            int shift = BitConverter.IsLittleEndian ? littleEndianShift : 24 - littleEndianShift;
            return Vector256.ConvertToSingle(((rgba >> shift) & Vector256.Create(255u)).AsInt32());
        }
    }
}
