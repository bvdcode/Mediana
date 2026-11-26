// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Video;

namespace Mediana.Codecs.Ffv1
{
    internal static class Ffv1PlaneDecoder
    {
        private static ReadOnlySpan<byte> RunExponents =>
            [0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7,
             8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24];

        internal static void Decode(scoped ref Ffv1RangeReader range, scoped ref Ffv1RiceReader bits,
            scoped Span<byte> pixels, PlaneLayout layout, bool riceCoding, Ffv1Quantizer quantizer, scoped Span<byte> state, CancellationToken token)
        {
            Span<byte> plane = pixels[layout.Offset..];
            int width = layout.RowBytes;
            int stride = layout.Stride;
            int runIndex = 0;
            Span<Ffv1RiceContext> rice = riceCoding ? MemoryMarshal.Cast<byte, Ffv1RiceContext>(state) : default;
            ReadOnlySpan<short> quantizers = quantizer.Values;
            for (int y = 0; y < layout.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                Span<byte> current = plane.Slice(y * stride, width);
                ReadOnlySpan<byte> above = y == 0 ? [] : plane.Slice((y - 1) * stride, width);
                ReadOnlySpan<byte> aboveTwo = y < 2 ? [] : plane.Slice((y - 2) * stride, width);
                int run = 0;
                bool inRun = false, lastRun = false;
                int left = above.IsEmpty ? 0 : above[0];
                int leftTwo = 0;
                int diagonal = aboveTwo.IsEmpty ? 0 : aboveTwo[0];
                for (int x = 0; x < width; x++)
                {
                    int top = above.IsEmpty ? 0 : above[x];
                    int right = above.IsEmpty ? 0 : above[Math.Min(x + 1, width - 1)];
                    int topTwo = aboveTwo.IsEmpty ? 0 : aboveTwo[x];
                    int context = quantizers[(left - diagonal) & 255] + quantizers[256 + ((diagonal - top) & 255)]
                        + quantizers[512 + ((top - right) & 255)] + quantizers[768 + ((leftTwo - left) & 255)]
                        + quantizers[1024 + ((topTwo - top) & 255)];
                    int absolute = Math.Abs(context);
                    int residual;
                    if (riceCoding)
                    {
                        inRun |= context == 0;
                        if (inRun)
                        {
                            if (run == 0 && !lastRun)
                            {
                                int exponent = RunExponents[runIndex];
                                if (bits.ReadBit())
                                {
                                    run = 1 << exponent;
                                    if (run <= width - x && runIndex < RunExponents.Length - 1)
                                    {
                                        runIndex++;
                                    }
                                }
                                else
                                {
                                    run = bits.Read(exponent);
                                    runIndex = Math.Max(0, runIndex - 1);
                                    lastRun = true;
                                }
                            }
                            if (run != 0)
                            {
                                run--;
                                residual = 0;
                            }
                            else
                            {
                                inRun = lastRun = false;
                                residual = rice[absolute].Read(ref bits);
                                if (residual >= 0)
                                {
                                    residual++;
                                }
                            }
                        }
                        else
                        {
                            residual = rice[absolute].Read(ref bits);
                        }
                    }
                    else
                    {
                        residual = range.ReadSymbol(state.Slice(absolute * Ffv1FrameModel.StateBytes,
                            Ffv1FrameModel.StateBytes), signed: true);
                    }
                    int prediction = Math.Clamp(left + top - diagonal, Math.Min(left, top), Math.Max(left, top));
                    leftTwo = left;
                    left = unchecked((byte)(prediction + (context < 0 ? -residual : residual)));
                    current[x] = (byte)left;
                    diagonal = top;
                }
            }
        }
    }
}
