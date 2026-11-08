// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Av1
{
    internal static class Av1GrainSynthesis
    {
        private const int BlockSize = 32;
        private const int Overhang = 2;

        internal static void Apply(Av1GrainParameters parameters, int width, int height, bool monochrome,
            bool identityMatrix, ReadOnlySpan<byte> source, IReadOnlyList<PlaneLayout> sourcePlanes,
            Span<byte> target, IReadOnlyList<PlaneLayout> targetPlanes, MediaBufferAllocator allocator, CancellationToken token) =>
            ApplyCore(parameters, width, height, 8, monochrome, identityMatrix, source, sourcePlanes, target, targetPlanes, allocator, token);

        internal static void Apply(Av1GrainParameters parameters, int width, int height, int bitDepth, bool monochrome,
            bool identityMatrix, ReadOnlySpan<ushort> source, IReadOnlyList<PlaneLayout> sourcePlanes,
            Span<ushort> target, IReadOnlyList<PlaneLayout> targetPlanes, MediaBufferAllocator allocator, CancellationToken token)
        {
            if (bitDepth is not (10 or 12)) { throw new ArgumentOutOfRangeException(nameof(bitDepth)); }
            ApplyCore(parameters, width, height, bitDepth, monochrome, identityMatrix, source, sourcePlanes, target, targetPlanes, allocator, token);
        }

        private static void ApplyCore<T>(Av1GrainParameters parameters, int width, int height, int bitDepth, bool monochrome,
            bool identityMatrix, ReadOnlySpan<T> source, IReadOnlyList<PlaneLayout> sourcePlanes,
            Span<T> target, IReadOnlyList<PlaneLayout> targetPlanes, MediaBufferAllocator allocator, CancellationToken token)
            where T : unmanaged, IBinaryInteger<T>
        {
            token.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            int planeCount = monochrome ? 1 : 3;
            int sampleBytes = Unsafe.SizeOf<T>();
            if (sourcePlanes.Count != planeCount || targetPlanes.Count != planeCount)
            {
                throw new ArgumentException("The film-grain plane count does not match the image.");
            }
            for (int plane = 0; plane < planeCount; plane++)
            {
                int planeWidth = plane == 0 ? width : (width + 1) >> 1;
                int planeHeight = plane == 0 ? height : (height + 1) >> 1;
                ValidateLayout(sourcePlanes[plane], planeWidth, planeHeight, sampleBytes, checked(source.Length * sampleBytes));
                ValidateLayout(targetPlanes[plane], planeWidth, planeHeight, sampleBytes, checked(target.Length * sampleBytes));
            }
            if (source.Overlaps(target)) { throw new ArgumentException("Film-grain output must not overwrite reference samples.", nameof(target)); }
            int paddedWidth = checked(((width - 1) / BlockSize + 1) * BlockSize + Overhang);
            int grainLength = Av1GrainNoise.LumaLength + (monochrome ? 0 : 2 * Av1GrainNoise.ChromaLength);
            int lutLength = planeCount * Av1GrainScaling.Length;
            int stripeLength = checked(paddedWidth * (monochrome ? BlockSize + Overhang : 51));
            int tailLength = parameters.Overlap ? checked(paddedWidth * (monochrome ? Overhang : 3)) : 0;
            using WritableMediaBuffer scratch = allocator.Allocate(checked((grainLength + lutLength + stripeLength + tailLength) * sizeof(short)));
            Span<short> storage = MemoryMarshal.Cast<byte, short>(scratch.Span);
            Span<short> grains = storage[..grainLength];
            Span<short> luts = storage.Slice(grainLength, lutLength);
            Span<short> stripe = storage.Slice(grainLength + lutLength, stripeLength);
            Span<short> tail = storage.Slice(grainLength + lutLength + stripeLength, tailLength);
            Span<short> lumaGrain = grains[..Av1GrainNoise.LumaLength];
            Span<short> uGrain = monochrome ? default : grains.Slice(Av1GrainNoise.LumaLength, Av1GrainNoise.ChromaLength);
            Span<short> vGrain = monochrome ? default : grains.Slice(Av1GrainNoise.LumaLength + Av1GrainNoise.ChromaLength, Av1GrainNoise.ChromaLength);
            Av1GrainNoise.Generate(parameters, bitDepth, monochrome, lumaGrain, uGrain, vGrain, token);
            Av1GrainScaling.Initialize(parameters.LumaPoints.Span, luts[..Av1GrainScaling.Length]);
            if (!monochrome)
            {
                Av1GrainScaling.Initialize(parameters.ChromaScalingFromLuma ? parameters.LumaPoints.Span : parameters.U.Points.Span,
                    luts.Slice(Av1GrainScaling.Length, Av1GrainScaling.Length));
                Av1GrainScaling.Initialize(parameters.ChromaScalingFromLuma ? parameters.LumaPoints.Span : parameters.V.Points.Span,
                    luts.Slice(2 * Av1GrainScaling.Length, Av1GrainScaling.Length));
            }
            int grainMinimum = -(128 << (bitDepth - 8));
            int grainMaximum = -grainMinimum - 1;
            for (int row = 0, index = 0; row < height; row += BlockSize, index++)
            {
                token.ThrowIfCancellationRequested();
                if (parameters.Overlap && index != 0) { CopyTail(stripe, tail, paddedWidth, planeCount); }
                FillStripe(parameters, index, width, paddedWidth, grains, stripe, planeCount, grainMinimum, grainMaximum);
                WriteStripe(parameters, row, width, height, paddedWidth, bitDepth, identityMatrix,
                    source, sourcePlanes, target, targetPlanes, luts, stripe, tail, planeCount, grainMinimum, grainMaximum, token);
            }
        }

        private static void ValidateLayout(PlaneLayout plane, int width, int height, int sampleBytes, int length)
        {
            plane.Validate(length);
            if (plane.RowBytes != width * sampleBytes || plane.Height != height || plane.Offset % sampleBytes != 0 || plane.Stride % sampleBytes != 0)
            {
                throw new ArgumentException("The film-grain plane layout does not match the sample geometry.");
            }
        }

        private static void CopyTail(ReadOnlySpan<short> stripe, Span<short> tail, int paddedWidth, int planes)
        {
            stripe.Slice(BlockSize * paddedWidth, Overhang * paddedWidth).CopyTo(tail);
            if (planes == 1) { return; }
            int stride = paddedWidth >> 1;
            int offset = (BlockSize + Overhang) * paddedWidth;
            stripe.Slice(offset + 16 * stride, stride).CopyTo(tail[(Overhang * paddedWidth)..]);
            stripe.Slice(offset + 33 * stride, stride).CopyTo(tail[(Overhang * paddedWidth + stride)..]);
        }

        private static void FillStripe(Av1GrainParameters parameters, int stripeIndex, int width, int paddedWidth,
            ReadOnlySpan<short> grains, Span<short> stripe, int planeCount, int minimum, int maximum)
        {
            int seed = parameters.Seed ^ (((stripeIndex * 37 + 178) & 255) << 8) ^ ((stripeIndex * 173 + 105) & 255);
            Av1GrainRandom random = new((ushort)seed);
            for (int block = 0; block < width; block += BlockSize)
            {
                int value = random.Next(8);
                for (int plane = 0; plane < planeCount; plane++)
                {
                    int shift = plane == 0 ? 0 : 1;
                    int stride = paddedWidth >> shift;
                    int size = (BlockSize + Overhang) >> shift;
                    int offset = plane == 0 ? 0 : (BlockSize + Overhang) * paddedWidth + (plane - 1) * size * stride;
                    int grainOffset = plane == 0 ? 0 : Av1GrainNoise.LumaLength + (plane - 1) * Av1GrainNoise.ChromaLength;
                    int grainStride = plane == 0 ? Av1GrainNoise.LumaWidth : Av1GrainNoise.ChromaWidth;
                    int x = plane == 0 ? 9 + (value >> 4) * 2 : 6 + (value >> 4);
                    int y = plane == 0 ? 9 + (value & 15) * 2 : 6 + (value & 15);
                    for (int row = 0; row < size; row++)
                    {
                        int destination = offset + row * stride + (block >> shift);
                        int source = grainOffset + (y + row) * grainStride + x;
                        for (int column = 0; column < size; column++)
                        {
                            int grain = grains[source + column];
                            if (parameters.Overlap && block != 0 && column < (Overhang >> shift))
                            {
                                grain = Blend(stripe[destination + column], grain, shift, column, minimum, maximum);
                            }
                            stripe[destination + column] = (short)grain;
                        }
                    }
                }
            }
        }

        private static void WriteStripe<T>(Av1GrainParameters parameters, int lumaRow, int width, int height, int paddedWidth,
            int bitDepth, bool identityMatrix, ReadOnlySpan<T> source, IReadOnlyList<PlaneLayout> sourcePlanes,
            Span<T> target, IReadOnlyList<PlaneLayout> targetPlanes, ReadOnlySpan<short> luts, ReadOnlySpan<short> stripe,
            ReadOnlySpan<short> tail, int planeCount, int grainMinimum, int grainMaximum, CancellationToken token)
            where T : unmanaged, IBinaryInteger<T>
        {
            int sampleBytes = Unsafe.SizeOf<T>();
            int precisionShift = bitDepth - 8;
            int sampleMaximum = (1 << bitDepth) - 1;
            int minimum = parameters.RestrictedRange ? 16 << precisionShift : 0;
            int lumaMaximum = parameters.RestrictedRange ? 235 << precisionShift : sampleMaximum;
            int chromaMaximum = parameters.RestrictedRange ? (identityMatrix ? 235 : 240) << precisionShift : sampleMaximum;
            ReadOnlySpan<T> luma = source[(sourcePlanes[0].Offset / sampleBytes)..];
            int lumaStride = sourcePlanes[0].Stride / sampleBytes;
            for (int plane = 0; plane < planeCount; plane++)
            {
                int shift = plane == 0 ? 0 : 1;
                int rowStart = lumaRow >> shift;
                int planeWidth = (width + shift) >> shift;
                int planeHeight = (height + shift) >> shift;
                int stride = paddedWidth >> shift;
                int stripeOffset = plane == 0 ? 0 : (BlockSize + Overhang) * paddedWidth + (plane - 1) * 17 * stride;
                int tailOffset = plane == 0 ? 0 : Overhang * paddedWidth + (plane - 1) * stride;
                Av1GrainChannel channel = plane switch { 0 => default, 1 => parameters.U, 2 => parameters.V, _ => throw new InvalidOperationException() };
                bool active = plane == 0 ? !parameters.LumaPoints.IsEmpty : parameters.ChromaScalingFromLuma || !channel.Points.IsEmpty;
                ReadOnlySpan<short> lut = luts.Slice(plane * Av1GrainScaling.Length, Av1GrainScaling.Length);
                int rows = Math.Min(BlockSize >> shift, planeHeight - rowStart);
                int maximum = plane == 0 ? lumaMaximum : chromaMaximum;
                for (int row = 0; row < rows; row++)
                {
                    token.ThrowIfCancellationRequested();
                    ReadOnlySpan<T> input = source.Slice((sourcePlanes[plane].Offset + (rowStart + row) * sourcePlanes[plane].Stride) / sampleBytes, planeWidth);
                    Span<T> output = target.Slice((targetPlanes[plane].Offset + (rowStart + row) * targetPlanes[plane].Stride) / sampleBytes, planeWidth);
                    if (!active) { input.CopyTo(output); continue; }
                    for (int x = 0; x < planeWidth; x++)
                    {
                        int original = int.CreateChecked(input[x]);
                        int merged = original;
                        if (plane != 0)
                        {
                            int location = ((rowStart + row) * 2) * lumaStride;
                            int left = int.CreateChecked(luma[location + x * 2]);
                            int right = int.CreateChecked(luma[location + Math.Min(x * 2 + 1, width - 1)]);
                            int average = Av1GrainNoise.Round(left + right, 1);
                            merged = parameters.ChromaScalingFromLuma ? average : Math.Clamp(((average * channel.LumaMultiplier + original * channel.Multiplier) >> 6)
                                + (channel.Offset << precisionShift), 0, sampleMaximum);
                        }
                        int noise = stripe[stripeOffset + row * stride + x];
                        if (parameters.Overlap && lumaRow != 0 && row < (Overhang >> shift))
                        {
                            noise = Blend(tail[tailOffset + row * stride + x], noise, shift, row, grainMinimum, grainMaximum);
                        }
                        int value = original + Av1GrainNoise.Round(Av1GrainScaling.At(lut, merged, bitDepth) * noise, parameters.ScalingShift);
                        output[x] = T.CreateTruncating(Math.Clamp(value, minimum, maximum));
                    }
                }
            }
        }

        private static int Blend(int previous, int current, int subsampling, int coordinate, int minimum, int maximum)
        {
            int result = subsampling switch
            {
                0 => coordinate switch { 0 => previous * 27 + current * 17, 1 => previous * 17 + current * 27, _ => throw new ArgumentOutOfRangeException(nameof(coordinate)) },
                1 => previous * 23 + current * 22,
                _ => throw new ArgumentOutOfRangeException(nameof(subsampling))
            };
            return Math.Clamp(Av1GrainNoise.Round(result, 5), minimum, maximum);
        }
    }
}
