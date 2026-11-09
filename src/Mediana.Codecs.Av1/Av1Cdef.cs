// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1Cdef
    {
        internal static int ScratchLength(Av1SequenceHeader sequence) => checked(sequence.Stride * (sequence.Monochrome ? 12 : 20));

        internal static void Apply<T>(Span<T> pixels, Av1SequenceHeader sequence, Av1CdefParameters parameters,
            ReadOnlySpan<Av1FilterBlock> blocks, ReadOnlySpan<sbyte> indices, Span<T> scratch, CancellationToken cancellationToken,
            int bitDepth = 8) where T : unmanaged, IBinaryInteger<T>
        {
            if (pixels.Length != sequence.SampleCount || blocks.Length != sequence.MiColumns * sequence.MiRows ||
                indices.Length != parameters.IndexCount(sequence) || scratch.Length != ScratchLength(sequence))
            {
                throw new ArgumentException("The AV1 directional-filter workspace has an invalid extent.");
            }
            int planes = sequence.Monochrome ? 1 : 3;
            for (int y = 0; y < sequence.PaddedHeight; y += 8)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int offset = 0;
                for (int plane = 0; plane < planes; plane++)
                {
                    int stride = sequence.PlaneStride(plane);
                    int side = 8 >> sequence.PlaneShift(plane);
                    int start = y >> sequence.PlaneShift(plane);
                    Span<T> ring = scratch.Slice(offset, stride * (side + 4));
                    if (y != 0) { ring.Slice(side * stride, 2 * stride).CopyTo(ring); }
                    int rows = Math.Min(side + 2, sequence.PlaneHeight(plane) - start);
                    pixels.Slice(sequence.PlaneOffset(plane) + start * stride, rows * stride).CopyTo(ring[(2 * stride)..]);
                    offset += ring.Length;
                }
                for (int x = 0; x < sequence.Stride; x += 8)
                {
                    int row = y / 4;
                    int column = x / 4;
                    int first = row * sequence.MiColumns + column;
                    if (blocks[first].Skip && blocks[first + 1].Skip && blocks[first + sequence.MiColumns].Skip && blocks[first + sequence.MiColumns + 1].Skip) { continue; }
                    int index = indices.IsEmpty ? 0 : indices[(row >> 4) * ((sequence.MiColumns + 15) >> 4) + (column >> 4)];
                    if (index < 0 || parameters.Strengths[index] == 0) { continue; }
                    int direction = Av1CdefDirection.Find<T>(scratch[(2 * sequence.Stride + x)..], sequence.Stride, out int variance, bitDepth);
                    int primary = parameters.Primary(index, 0);
                    int yDirection = primary == 0 ? 0 : direction;
                    Av1CdefKernel.Filter(scratch, pixels[..sequence.LumaSamples], sequence.Stride, sequence.PaddedHeight,
                        x, y, 8, primary, parameters.Secondary(index, 0), parameters.Damping, yDirection, bitDepth, variance);
                    offset = sequence.Stride * 12;
                    for (int plane = 1; plane < planes; plane++)
                    {
                        int stride = sequence.PlaneStride(plane);
                        primary = parameters.Primary(index, plane);
                        Av1CdefKernel.Filter(scratch.Slice(offset, stride * 8), pixels[sequence.PlaneOffset(plane)..], stride, sequence.PlaneHeight(plane),
                            x >> 1, y >> 1, 4, primary, parameters.Secondary(index, plane), parameters.Damping - 1, primary == 0 ? 0 : direction, bitDepth);
                        offset += stride * 8;
                    }
                }
            }
        }
    }
}
