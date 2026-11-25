// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264SampleReconstruction
    {
        public static void Write(ReadOnlySpan<byte> prediction, int predictionStride, ReadOnlySpan<int> residual, int size, Span<byte> pixels, int stride)
        {
            if (size == 8 && Vector128.IsHardwareAccelerated)
            {
                for (int row = 0; row < 8; row++)
                {
                    ReadOnlySpan<int> values = residual[(row * 8)..];
                    Write8Row(prediction[(row * predictionStride)..], Vector128.Create(values),
                        Vector128.Create(values[4..]), pixels[(row * stride)..]);
                }
                return;
            }
            if (Vector128.IsHardwareAccelerated)
            {
                for (int row = 0; row < size; row++)
                {
                    for (int column = 0; column < size; column += 4)
                    {
                        int offset = row * size + column;
                        Vector128<byte> samples = Reconstruct(ReadFour(prediction[(row * predictionStride + column)..]),
                            Vector128.Create(residual[offset..]), Vector128<int>.Zero);
                        WriteFour(samples, pixels[(row * stride + column)..]);
                    }
                }
                return;
            }
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    int offset = row * size + column;
                    pixels[row * stride + column] = (byte)Math.Clamp((long)prediction[row * predictionStride + column] + residual[offset], 0, 255);
                }
            }
        }

        public static long WriteAndMeasure(ReadOnlySpan<byte> source, ReadOnlySpan<byte> prediction, int stride,
            ReadOnlySpan<int> residual, Span<byte> reconstruction)
        {
            if (Vector128.IsHardwareAccelerated)
            {
                Vector128<int> squared = Vector128<int>.Zero;
                for (int row = 0; row < 4; row++)
                {
                    int offset = row * stride;
                    Vector128<byte> samples = Reconstruct(ReadFour(prediction[offset..]), Vector128.Create(residual[(row * 4)..]), Vector128<int>.Zero);
                    WriteFour(samples, reconstruction[offset..]);
                    Vector128<short> difference = Vector128.WidenLower(ReadFour(source[offset..])).AsInt16()
                        - Vector128.WidenLower(samples).AsInt16();
                    if (Sse2.IsSupported)
                    {
                        squared += Sse2.MultiplyAddAdjacent(difference, difference);
                    }
                    else
                    {
                        Vector128<int> widened = Vector128.WidenLower(difference);
                        squared += widened * widened;
                    }
                }
                return Vector128.Sum(squared);
            }
            long error = 0;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    int offset = row * stride + column;
                    int sample = (int)Math.Clamp((long)prediction[offset] + residual[row * 4 + column], 0, 255);
                    reconstruction[offset] = (byte)sample;
                    int difference = source[offset] - sample;
                    error += difference * difference;
                }
            }
            return error;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Write8Row(ReadOnlySpan<byte> prediction, Vector128<int> lower, Vector128<int> upper, Span<byte> destination)
        {
            Vector128<byte> samples = Vector128.CreateScalar(MemoryMarshal.Read<ulong>(prediction)).AsByte();
            ulong reconstructed = Reconstruct(samples, lower, upper).AsUInt64().GetElement(0);
            MemoryMarshal.Write(destination, in reconstructed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Write4Row(ReadOnlySpan<byte> prediction, Vector128<int> residual, Span<byte> destination) =>
            WriteFour(Reconstruct(ReadFour(prediction), residual, Vector128<int>.Zero), destination);

        private static Vector128<byte> Reconstruct(Vector128<byte> prediction, Vector128<int> lowerResidual, Vector128<int> upperResidual)
        {
            Vector128<short> bounded = Vector128.NarrowWithSaturation(lowerResidual, upperResidual);
            Vector128<short> samples = Vector128.AddSaturate(Vector128.WidenLower(prediction).AsInt16(), bounded);
            if (Sse2.IsSupported)
            {
                return Sse2.PackUnsignedSaturate(samples, Vector128<short>.Zero);
            }
            return Vector128.NarrowWithSaturation(Vector128.Max(samples, Vector128<short>.Zero).AsUInt16(), Vector128<ushort>.Zero);
        }

        private static Vector128<byte> ReadFour(ReadOnlySpan<byte> samples) =>
            Vector128.CreateScalar(MemoryMarshal.Read<uint>(samples)).AsByte();

        private static void WriteFour(Vector128<byte> samples, Span<byte> output)
        {
            uint word = samples.AsUInt32().GetElement(0);
            MemoryMarshal.Write(output, in word);
        }
    }
}
