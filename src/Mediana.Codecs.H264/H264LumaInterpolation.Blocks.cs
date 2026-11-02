// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal static partial class H264LumaInterpolation
    {
        private static void FilterHalfBlock(ReadOnlySpan<byte> samples, int stride, int origin, int tapStride,
            int width, int height, int outputStride, Span<byte> output, bool average)
        {
            for (int row = 0; row < height; row++)
            {
                int start = origin + row * stride - FirstTap * tapStride;
                int destination = row * outputStride;
                int column = 0;
                for (; column <= width - Vector64<byte>.Count; column += Vector64<byte>.Count)
                {
                    Vector128<byte> value = H264LumaFilter.Half8(H264LumaFilter.Sum8(samples, start + column, tapStride));
                    Write8(output, destination + column, value, average);
                }
                if (column <= width - Vector128<int>.Count)
                {
                    Vector128<byte> value = H264LumaFilter.Half8(H264LumaFilter.Sum4(samples, start + column, tapStride));
                    Write4(output, destination + column, value, average);
                    column += Vector128<int>.Count;
                }
                for (; column < width; column++)
                {
                    int value = Half(H264LumaFilter.Sum(samples, start + column, tapStride));
                    WriteSample(output, destination + column, value, average);
                }
            }
        }

        private static void PrepareHorizontal(ReadOnlySpan<byte> samples, int stride, int width, int height, Span<int> horizontal)
        {
            for (int row = 0; row < height + Padding; row++)
            {
                int column = 0;
                for (; column <= width - Vector128<short>.Count; column += Vector128<short>.Count)
                {
                    Vector128<short> sum = H264LumaFilter.Sum8(samples, row * stride + column, 1);
                    Vector128.WidenLower(sum).StoreUnsafe(ref MemoryMarshal.GetReference(horizontal), (nuint)(row * width + column));
                    Vector128.WidenUpper(sum).StoreUnsafe(ref MemoryMarshal.GetReference(horizontal), (nuint)(row * width + column + Vector128<int>.Count));
                }
                if (column <= width - Vector128<int>.Count)
                {
                    Vector128<short> sum = H264LumaFilter.Sum4(samples, row * stride + column, 1);
                    Vector128.WidenLower(sum).StoreUnsafe(ref MemoryMarshal.GetReference(horizontal), (nuint)(row * width + column));
                    column += Vector128<int>.Count;
                }
                for (; column < width; column++)
                {
                    horizontal[row * width + column] = HorizontalSum(samples, row * stride + column);
                }
            }
        }

        private static void FilterDiagonalBlock(ReadOnlySpan<int> horizontal, int width, int height, int outputStride, Span<byte> output)
        {
            for (int row = 0; row < height; row++)
            {
                int column = 0;
                for (; column <= width - Vector64<byte>.Count; column += Vector64<byte>.Count)
                {
                    H264VectorSamples.Write8(output, row * outputStride + column, H264LumaFilter.Diagonal8(horizontal, width, row * width + column));
                }
                if (column <= width - Vector128<int>.Count)
                {
                    Write4(output, row * outputStride + column, H264LumaFilter.Diagonal4Samples(horizontal, width, row * width + column), false);
                    column += Vector128<int>.Count;
                }
                for (; column < width; column++)
                {
                    output[row * outputStride + column] = (byte)Diagonal(horizontal, width, row * width + column);
                }
            }
        }

        private static void AverageHorizontalBlock(ReadOnlySpan<int> horizontal, int width, int firstRow,
            int height, int outputStride, Span<byte> output)
        {
            for (int row = 0; row < height; row++)
            {
                int start = (row + firstRow) * width;
                int column = 0;
                for (; column <= width - Vector128<short>.Count; column += Vector128<short>.Count)
                {
                    Vector128<int> lower = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(horizontal), (nuint)(start + column));
                    Vector128<int> upper = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(horizontal), (nuint)(start + column + Vector128<int>.Count));
                    Write8(output, row * outputStride + column, H264LumaFilter.Half8(Vector128.Narrow(lower, upper)), true);
                }
                if (column <= width - Vector128<int>.Count)
                {
                    Vector128<int> values = Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(horizontal), (nuint)(start + column));
                    Vector128<byte> value = H264LumaFilter.Half8(Vector128.Narrow(values, Vector128<int>.Zero));
                    Write4(output, row * outputStride + column, value, true);
                    column += Vector128<int>.Count;
                }
                for (; column < width; column++)
                {
                    WriteSample(output, row * outputStride + column, Half(horizontal[start + column]), true);
                }
            }
        }

        private static void AverageIntegerBlock(ReadOnlySpan<byte> samples, int stride, int origin,
            int width, int height, int outputStride, Span<byte> output)
        {
            for (int row = 0; row < height; row++)
            {
                int column = 0;
                for (; column <= width - Vector64<byte>.Count; column += Vector64<byte>.Count)
                {
                    Vector128<byte> value = H264VectorSamples.Read8(samples, origin + row * stride + column);
                    Write8(output, row * outputStride + column, value, true);
                }
                if (column <= width - Vector128<int>.Count)
                {
                    uint value = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref MemoryMarshal.GetReference(samples), origin + row * stride + column));
                    Write4(output, row * outputStride + column, Vector128.CreateScalar(value).AsByte(), true);
                    column += Vector128<int>.Count;
                }
                for (; column < width; column++)
                {
                    WriteSample(output, row * outputStride + column, samples[origin + row * stride + column], true);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Write8(Span<byte> output, int position, Vector128<byte> value, bool average)
        {
            if (average)
            {
                value = H264SampleAverage.Rounded(value, H264VectorSamples.Read8(output, position));
            }
            H264VectorSamples.Write8(output, position, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Write4(Span<byte> output, int position, Vector128<byte> value, bool average)
        {
            ref byte destination = ref Unsafe.Add(ref MemoryMarshal.GetReference(output), position);
            if (average)
            {
                Vector128<byte> previous = Vector128.CreateScalar(Unsafe.ReadUnaligned<uint>(ref destination)).AsByte();
                value = H264SampleAverage.Rounded(value, previous);
            }
            Unsafe.WriteUnaligned(ref destination, value.AsUInt32().ToScalar());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteSample(Span<byte> output, int position, int value, bool average)
        {
            output[position] = (byte)(average ? Average(output[position], value) : value);
        }
    }
}
