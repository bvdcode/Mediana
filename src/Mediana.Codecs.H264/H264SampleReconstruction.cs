// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mediana.Codecs.H264
{
    internal static class H264SampleReconstruction
    {
        public static void Write(ReadOnlySpan<byte> prediction, ReadOnlySpan<int> residual, int size, Span<byte> pixels, int stride)
        {
            if (Vector128.IsHardwareAccelerated)
            {
                for (int row = 0; row < size; row++)
                {
                    for (int column = 0; column < size; column += 4)
                    {
                        int offset = row * size + column;
                        Vector128<byte> samples = Reconstruct(ReadFour(prediction[offset..]), residual.Slice(offset, 4));
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
                    pixels[row * stride + column] = (byte)Math.Clamp((long)prediction[offset] + residual[offset], 0, 255);
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
                    Vector128<byte> samples = Reconstruct(ReadFour(prediction[offset..]), residual.Slice(row * 4, 4));
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

        private static Vector128<byte> Reconstruct(Vector128<byte> prediction, ReadOnlySpan<int> residual)
        {
            Vector128<int> values = Vector128.Create(residual);
            if (Sse2.IsSupported)
            {
                Vector128<short> bounded = Sse2.PackSignedSaturate(values, Vector128<int>.Zero);
                Vector128<short> samples = Sse2.AddSaturate(Vector128.WidenLower(prediction).AsInt16(), bounded);
                return Sse2.PackUnsignedSaturate(samples, Vector128<short>.Zero);
            }
            Vector128<int> clipped = Vector128.Clamp(values, Vector128.Create(-255), Vector128.Create(255));
            Vector128<int> sums = Vector128.WidenLower(Vector128.WidenLower(prediction)).AsInt32() + clipped;
            Vector128<uint> samples32 = Vector128.Clamp(sums, Vector128<int>.Zero, Vector128.Create(255)).AsUInt32();
            Vector128<ushort> samples16 = Vector128.Narrow(samples32, Vector128<uint>.Zero);
            return Vector128.Narrow(samples16, Vector128<ushort>.Zero);
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
