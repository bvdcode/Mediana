// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Buffers.Binary;
using Mediana.Core.Video;

namespace Mediana.Codecs.Webp
{
    internal static class WebpPredictor
    {
        internal const int BlockBits = 5;
        private const int BlockSize = 1 << BlockBits;

        internal static bool TryReadUniform(VideoFrame input, CancellationToken token, out uint value)
        {
            uint stored = MemoryMarshal.Cast<byte, uint>(input.GetRow(0, 0))[0];
            value = BitConverter.IsLittleEndian ? stored : BinaryPrimitives.ReverseEndianness(stored);
            for (int row = 0; row < input.Format.Height; row++)
            {
                token.ThrowIfCancellationRequested();
                if (MemoryMarshal.Cast<byte, uint>(input.GetRow(0, row)).IndexOfAnyExcept(stored) >= 0) { return false; }
            }
            return true;
        }

        internal static bool ReadPixels(VideoFrame input, Span<uint> pixels, CancellationToken token)
        {
            bool alpha = false;
            for (int row = 0; row < input.Format.Height; row++)
            {
                token.ThrowIfCancellationRequested();
                ReadOnlySpan<uint> source = MemoryMarshal.Cast<byte, uint>(input.GetRow(0, row));
                Span<uint> output = pixels.Slice(row * source.Length, source.Length);
                int index = 0;
                if (BitConverter.IsLittleEndian && Vector256.IsHardwareAccelerated)
                {
                    for (; index <= source.Length - Vector256<uint>.Count; index += Vector256<uint>.Count)
                    {
                        Vector256<uint> value = Vector256.LoadUnsafe(ref MemoryMarshal.GetReference(source), (nuint)index);
                        alpha |= !Vector256.EqualsAll(value | Vector256.Create(0x00ffffffu), Vector256.Create(uint.MaxValue));
                        Vector256<uint> green = (value >> 8) & Vector256.Create(255u);
                        Vector256<uint> rb = ((value & Vector256.Create(0x00ff00ffu)) + Vector256.Create(0x01000100u) - (green | (green << 16)))
                            & Vector256.Create(0x00ff00ffu);
                        (rb | (value & Vector256.Create(0xff00ff00u))).StoreUnsafe(ref MemoryMarshal.GetReference(output), (nuint)index);
                    }
                }
                for (; index < source.Length; index++)
                {
                    uint value = BitConverter.IsLittleEndian ? source[index] : BinaryPrimitives.ReverseEndianness(source[index]);
                    uint green = (value >> 8) & 255;
                    alpha |= value >> 24 != 255;
                    output[index] = (((value & 0x00ff00ff) + 0x01000100 - (green | (green << 16))) & 0x00ff00ff) | (value & 0xff00ff00);
                }
            }
            return alpha;
        }

        internal static void Transform(Span<uint> pixels, int width, int height, Span<uint> modes, CancellationToken token)
        {
            int columns = (width + BlockSize - 1) >> BlockBits;
            Span<long> costs = stackalloc long[4];
            for (int by = 0; by < height; by += BlockSize)
            {
                token.ThrowIfCancellationRequested();
                for (int bx = 0; bx < width; bx += BlockSize)
                {
                    costs.Clear();
                    for (int y = Math.Max(1, by); y < Math.Min(height, by + BlockSize); y += 4)
                    {
                        for (int x = Math.Max(1, bx); x < Math.Min(width, bx + BlockSize); x += 4)
                        {
                            int index = y * width + x;
                            uint value = pixels[index];
                            costs[0] += Cost(Subtract(value, 0xff000000));
                            costs[1] += Cost(Subtract(value, pixels[index - 1]));
                            costs[2] += Cost(Subtract(value, pixels[index - width]));
                            costs[3] += Cost(Subtract(value, Gradient(pixels[index - 1], pixels[index - width], pixels[index - width - 1])));
                        }
                    }
                    int best = 0;
                    for (int index = 1; index < costs.Length; index++) { if (costs[index] < costs[best]) { best = index; } }
                    int mode = best switch
                    {
                        0 => 0,
                        1 => 1,
                        2 => 2,
                        3 => 12,
                        _ => throw new InvalidOperationException("Invalid WebP predictor selection.")
                    };
                    modes[(by >> BlockBits) * columns + (bx >> BlockBits)] = 0xff000000u | ((uint)mode << 8);
                }
            }
            for (int y = height - 1; y >= 0; y--)
            {
                token.ThrowIfCancellationRequested();
                int offset = y * width;
                for (int x = width - 1; x >= 1;)
                {
                    int mode = y == 0 ? 1 : (int)((modes[(y >> BlockBits) * columns + (x >> BlockBits)] >> 8) & 255);
                    int start = Math.Max(1, (x >> BlockBits) << BlockBits);
                    if (Vector128.IsHardwareAccelerated)
                    {
                        for (; x >= start + 3; x -= 4)
                        {
                            int index = offset + x - 3;
                            Vector128<byte> value = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)index).AsByte();
                            Vector128<byte> predicted = PredictVector(pixels, index, width, mode);
                            (value - predicted).AsUInt32().StoreUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)index);
                        }
                    }
                    for (; x >= start; x--)
                    {
                        int index = offset + x;
                        uint predicted = mode switch
                        {
                            0 => 0xff000000,
                            1 => pixels[index - 1],
                            2 => pixels[index - width],
                            12 => Gradient(pixels[index - 1], pixels[index - width], pixels[index - width - 1]),
                            _ => throw new InvalidOperationException("Invalid WebP prediction mode.")
                        };
                        pixels[index] = Subtract(pixels[index], predicted);
                    }
                }
                pixels[offset] = Subtract(pixels[offset], y == 0 ? 0xff000000 : pixels[offset - width]);
            }
        }

        private static Vector128<byte> PredictVector(Span<uint> pixels, int index, int width, int mode)
        {
            switch (mode)
            {
                case 0: return Vector128.Create(0xff000000u).AsByte();
                case 1: return Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(index - 1)).AsByte();
                case 2: return Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(index - width)).AsByte();
                case 12:
                    Vector128<byte> left = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(index - 1)).AsByte();
                    Vector128<byte> top = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(index - width)).AsByte();
                    Vector128<byte> corner = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(pixels), (nuint)(index - width - 1)).AsByte();
                    Vector128<short> lower = Vector128.WidenLower(left).AsInt16() + Vector128.WidenLower(top).AsInt16() - Vector128.WidenLower(corner).AsInt16();
                    Vector128<short> upper = Vector128.WidenUpper(left).AsInt16() + Vector128.WidenUpper(top).AsInt16() - Vector128.WidenUpper(corner).AsInt16();
                    lower = Vector128.Clamp(lower, Vector128<short>.Zero, Vector128.Create((short)255));
                    upper = Vector128.Clamp(upper, Vector128<short>.Zero, Vector128.Create((short)255));
                    return Vector128.Narrow(lower.AsUInt16(), upper.AsUInt16());
                default: throw new InvalidOperationException("Invalid WebP prediction mode.");
            }
        }

        internal static uint Subtract(uint value, uint predicted) =>
            (((value & 0x00ff00ff) + 0x01000100 - (predicted & 0x00ff00ff)) & 0x00ff00ff)
            | (((((value >> 8) & 0x00ff00ff) + 0x01000100 - ((predicted >> 8) & 0x00ff00ff)) & 0x00ff00ff) << 8);

        private static uint Gradient(uint left, uint top, uint corner)
        {
            uint result = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                int predicted = (int)((left >> shift) & 255) + (int)((top >> shift) & 255) - (int)((corner >> shift) & 255);
                result |= (uint)Math.Clamp(predicted, 0, 255) << shift;
            }
            return result;
        }

        private static int Cost(uint value) => Math.Abs((int)unchecked((sbyte)value)) + Math.Abs((int)unchecked((sbyte)(value >> 8)))
            + Math.Abs((int)unchecked((sbyte)(value >> 16))) + Math.Abs((int)unchecked((sbyte)(value >> 24)));
    }
}
