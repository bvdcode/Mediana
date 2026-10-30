// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Mediana.Processing.Audio
{
    internal static class Pcm16Converter
    {
        private const int CancellationInterval = 2048;
        private const int FractionBits = 16;
        private const int Scale = 1 << 15;

        public static void Convert(ReadOnlySpan<float> input, Span<short> output, CancellationToken token)
        {
            int index = 0;
            for (; index <= input.Length - Vector128<short>.Count; index += Vector128<short>.Count)
            {
                if (index % CancellationInterval == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                Vector128<float> first = Vector128.Create(input[index..]);
                Vector128<float> second = Vector128.Create(input[(index + Vector128<float>.Count)..]);
                Vector128<short> result = Vector128.NarrowWithSaturation(Quantize(first), Quantize(second));
                result.CopyTo(output[index..]);
            }
            for (; index < input.Length; index++)
            {
                float value = input[index];
                if (!float.IsFinite(value))
                {
                    throw new InvalidDataException("Audio processing requires finite PCM samples.");
                }
                output[index] = (short)Math.Clamp(Math.Round(Math.Clamp((double)value, -1, 1) * Scale), short.MinValue, short.MaxValue);
            }
        }

        public static void Convert(ReadOnlySpan<int> input, Span<short> output, CancellationToken token)
        {
            int index = 0;
            for (; index <= input.Length - Vector128<short>.Count; index += Vector128<short>.Count)
            {
                if (index % CancellationInterval == 0)
                {
                    token.ThrowIfCancellationRequested();
                }
                Vector128<int> first = Vector128.Create(input[index..]);
                Vector128<int> second = Vector128.Create(input[(index + Vector128<int>.Count)..]);
                Vector128<short> result = Vector128.NarrowWithSaturation(Quantize(first), Quantize(second));
                result.CopyTo(output[index..]);
            }
            for (; index < input.Length; index++)
            {
                output[index] = (short)Math.Clamp(Math.Round(input[index] / (double)(1 << FractionBits)), short.MinValue, short.MaxValue);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> Quantize(Vector128<float> value)
        {
            if (!Vector128.LessThanOrEqualAll(Vector128.Abs(value), Vector128.Create(float.MaxValue)))
            {
                throw new InvalidDataException("Audio processing requires finite PCM samples.");
            }
            Vector128<float> scaled = Vector128.Clamp(value, Vector128.Create(-1f), Vector128.Create(1f)) * Scale;
            return Vector128.ConvertToInt32Native(Vector128.Round(scaled));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<int> Quantize(Vector128<int> value)
        {
            Vector128<int> whole = Vector128.ShiftRightArithmetic(value, FractionBits);
            Vector128<int> fraction = value & Vector128.Create((int)ushort.MaxValue);
            Vector128<int> halfway = Vector128.Create(Scale);
            Vector128<int> one = Vector128.Create(1);
            Vector128<int> increment = Vector128.GreaterThan(fraction, halfway) |
                (Vector128.Equals(fraction, halfway) & Vector128.Equals(whole & one, one));
            return whole + (increment & one);
        }
    }
}
