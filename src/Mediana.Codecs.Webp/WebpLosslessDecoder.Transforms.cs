// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

namespace Mediana.Codecs.Webp
{
    internal static partial class WebpLosslessDecoder
    {
        private static void Inverse(Span<uint> pixels, int height, WebpLosslessTransform transform, CancellationToken token)
        {
            int width = transform.Width, bits = transform.Bits, columns = Subsample(width, bits);
            ReadOnlySpan<uint> data = MemoryMarshal.Cast<byte, uint>(transform.Data.Span);
            switch (transform.Type)
            {
                case 0:
                    for (int y = 0; y < height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int x = 0; x < width; x++)
                        {
                            int index = y * width + x;
                            uint predicted;
                            if (y == 0) { predicted = x == 0 ? 0xff000000u : pixels[index - 1]; }
                            else if (x == 0) { predicted = pixels[index - width]; }
                            else
                            {
                                uint left = pixels[index - 1], top = pixels[index - width], corner = pixels[index - width - 1];
                                uint right = x == width - 1 ? pixels[y * width] : pixels[index - width + 1];
                                int mode = (int)((data[(y >> bits) * columns + (x >> bits)] >> 8) & 255);
                                predicted = mode switch
                                {
                                    0 => 0xff000000u,
                                    1 => left,
                                    2 => top,
                                    3 => right,
                                    4 => corner,
                                    5 => Average(Average(left, right), top),
                                    6 => Average(left, corner),
                                    7 => Average(left, top),
                                    8 => Average(corner, top),
                                    9 => Average(top, right),
                                    10 => Average(Average(left, corner), Average(top, right)),
                                    11 => Select(left, top, corner),
                                    12 => Gradient(left, top, corner),
                                    13 => Half(Average(left, top), corner),
                                    _ => throw new InvalidDataException("Invalid VP8L predictor mode.")
                                };
                            }
                            pixels[index] = Add(pixels[index], predicted);
                        }
                    }
                    break;
                case 1:
                    for (int y = 0; y < height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int x = 0; x < width; x++)
                        {
                            uint coefficients = data[(y >> bits) * columns + (x >> bits)], pixel = pixels[y * width + x];
                            int green = unchecked((sbyte)(pixel >> 8));
                            int red = (int)((pixel >> 16) & 255) + ((unchecked((sbyte)coefficients) * green) >> 5);
                            red &= 255;
                            int blue = (int)(pixel & 255) + ((unchecked((sbyte)(coefficients >> 8)) * green) >> 5)
                                + ((unchecked((sbyte)(coefficients >> 16)) * unchecked((sbyte)red)) >> 5);
                            pixels[y * width + x] = (pixel & 0xff00ff00u) | ((uint)red << 16) | (uint)(blue & 255);
                        }
                    }
                    break;
                case 2:
                    for (int y = 0; y < height; y++)
                    {
                        token.ThrowIfCancellationRequested();
                        Span<uint> row = pixels.Slice(y * width, width);
                        for (int x = 0; x < width; x++)
                        {
                            uint value = row[x], green = (value >> 8) & 255;
                            row[x] = Add(value, green | (green << 16));
                        }
                    }
                    break;
                case 3:
                    int packedWidth = columns, indexBits = 8 >> bits, mask = (1 << indexBits) - 1;
                    for (int y = height - 1; y >= 0; y--)
                    {
                        token.ThrowIfCancellationRequested();
                        int previous = -1, packed = 0;
                        for (int x = width - 1; x >= 0; x--)
                        {
                            int source = y * packedWidth + (x >> bits);
                            if (source != previous) { packed = (int)((pixels[source] >> 8) & 255); previous = source; }
                            int index = (packed >> ((x & ((1 << bits) - 1)) * indexBits)) & mask;
                            pixels[y * width + x] = data[index];
                        }
                    }
                    break;
                default: throw new InvalidDataException("Invalid VP8L inverse transform.");
            }
        }
        private static uint Add(uint a, uint b) => (((a & 0x00ff00ff) + (b & 0x00ff00ff)) & 0x00ff00ff)
            | (((a & 0xff00ff00) + (b & 0xff00ff00)) & 0xff00ff00);
        private static uint Average(uint a, uint b) => (a & b) + (((a ^ b) & 0xfefefefeu) >> 1);
        private static uint Gradient(uint left, uint top, uint corner)
        {
            uint result = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                int value = (int)((left >> shift) & 255) + (int)((top >> shift) & 255) - (int)((corner >> shift) & 255);
                result |= (uint)Math.Clamp(value, 0, 255) << shift;
            }
            return result;
        }
        private static uint Half(uint average, uint corner)
        {
            uint result = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                int value = (int)((average >> shift) & 255), other = (int)((corner >> shift) & 255);
                result |= (uint)Math.Clamp(value + (value - other) / 2, 0, 255) << shift;
            }
            return result;
        }
        private static uint Select(uint left, uint top, uint corner)
        {
            int leftDistance = 0, topDistance = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                int a = (int)((left >> shift) & 255), b = (int)((top >> shift) & 255), c = (int)((corner >> shift) & 255);
                leftDistance += Math.Abs(b - c); topDistance += Math.Abs(a - c);
            }
            return leftDistance < topDistance ? left : top;
        }
    }
}
