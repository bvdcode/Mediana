// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static class Av1Restoration
    {
        internal static int BoundaryLength(Av1SequenceHeader sequence, Av1RestorationParameters parameters)
        {
            int count = 0;
            for (int plane = 0; plane < (sequence.Monochrome ? 1 : 3); plane++)
            {
                if (parameters.Type(plane) != Av1RestorationType.None) { count = checked(count + BoundaryCount(sequence, plane) * sequence.PlaneStride(plane) * 4); }
            }
            return count;
        }

        internal static int ScratchLength(Av1SequenceHeader sequence, Av1RestorationParameters parameters)
        {
            int count = 0;
            for (int plane = 0; plane < (sequence.Monochrome ? 1 : 3); plane++)
            {
                if (parameters.Type(plane) != Av1RestorationType.None) { count = Math.Max(count, checked(sequence.PlaneStride(plane) * ((4 >> sequence.PlaneShift(plane)) + 6))); }
            }
            return count;
        }

        internal static void SaveBoundaries(ReadOnlySpan<byte> pixels, Av1SequenceHeader sequence, Av1RestorationParameters parameters, Span<byte> boundaries)
        {
            if (pixels.Length != sequence.SampleCount || boundaries.Length != BoundaryLength(sequence, parameters))
            {
                throw new ArgumentException("The AV1 restoration boundary workspace has an invalid extent.");
            }
            int offset = 0;
            for (int plane = 0; plane < (sequence.Monochrome ? 1 : 3); plane++)
            {
                if (parameters.Type(plane) == Av1RestorationType.None) { continue; }
                int shift = sequence.PlaneShift(plane);
                int stride = sequence.PlaneStride(plane);
                int height = (sequence.Height + shift) >> shift;
                int stripe = 64 >> shift;
                for (int boundary = 0; boundary < BoundaryCount(sequence, plane); boundary++)
                {
                    int row = (boundary + 1) * stripe - stripe / 8;
                    for (int line = -2; line < 2; line++)
                    {
                        int source = Math.Min(row + line, height - 1);
                        pixels.Slice(sequence.PlaneOffset(plane) + source * stride, stride).CopyTo(boundaries.Slice(offset, stride));
                        offset += stride;
                    }
                }
            }
        }

        internal static void Apply(Span<byte> pixels, Av1SequenceHeader sequence, Av1RestorationParameters parameters,
            ReadOnlySpan<Av1RestorationUnit> units, ReadOnlySpan<byte> boundaries, Span<byte> scratch, CancellationToken cancellationToken)
        {
            if (pixels.Length != sequence.SampleCount || units.Length != parameters.UnitCount(sequence) ||
                boundaries.Length != BoundaryLength(sequence, parameters) || scratch.Length != ScratchLength(sequence, parameters))
            {
                throw new ArgumentException("The AV1 restoration workspace has an invalid extent.");
            }
            int boundaryOffset = 0;
            for (int plane = 0; plane < (sequence.Monochrome ? 1 : 3); plane++)
            {
                if (parameters.Type(plane) == Av1RestorationType.None) { continue; }
                int shift = sequence.PlaneShift(plane);
                int side = 4 >> shift;
                int stride = sequence.PlaneStride(plane);
                int width = (sequence.Width + shift) >> shift;
                int height = (sequence.Height + shift) >> shift;
                int columns = parameters.Columns(sequence, plane);
                int rows = parameters.Rows(sequence, plane);
                int size = parameters.Size(plane);
                int unitOffset = parameters.Offset(sequence, plane);
                int boundaryLength = BoundaryCount(sequence, plane) * 4 * stride;
                ReadOnlySpan<byte> planeBoundaries = boundaries.Slice(boundaryOffset, boundaryLength);
                boundaryOffset += boundaryLength;
                Span<byte> ring = scratch[..(stride * (side + 6))];
                Span<byte> output = pixels.Slice(sequence.PlaneOffset(plane), sequence.LumaSamples >> (2 * shift));
                for (int y = 0; y < height; y += side)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (y != 0) { ring.Slice(side * stride, 3 * stride).CopyTo(ring); }
                    int copyRows = Math.Min(side + 3, height - y);
                    output.Slice(y * stride, copyRows * stride).CopyTo(ring[(3 * stride)..]);
                    Av1RestorationSource source = new(ring, planeBoundaries, stride, width, height, y, (y + (8 >> shift)) / (64 >> shift), 64 >> shift);
                    int unitRow = Math.Min(rows - 1, (y + (8 >> shift)) / size);
                    for (int x = 0; x < width;)
                    {
                        int unitColumn = Math.Min(columns - 1, x / size);
                        Av1RestorationUnit unit = units[unitOffset + unitRow * columns + unitColumn];
                        int end = unitColumn == columns - 1 ? width : (unitColumn + 1) * size;
                        int blockWidth = Math.Min(64 >> shift, end - x);
                        int blockHeight = Math.Min(side, height - y);
                        switch (unit.Type)
                        {
                            case Av1RestorationType.None:
                                break;
                            case Av1RestorationType.Wiener:
                                Av1RestorationWiener.Filter(source, output, stride, x, y, blockWidth, blockHeight, unit);
                                break;
                            case Av1RestorationType.SelfGuided:
                                Av1RestorationSelfGuided.Filter(source, output, stride, x, y, blockWidth, blockHeight, unit);
                                break;
                            case Av1RestorationType.Switchable:
                            default:
                                throw new InvalidDataException("The AV1 restoration unit type is invalid.");
                        }
                        x += blockWidth;
                    }
                }
            }
        }

        private static int BoundaryCount(Av1SequenceHeader sequence, int plane)
        {
            int shift = sequence.PlaneShift(plane);
            int stripe = 64 >> shift;
            int height = (sequence.Height + shift) >> shift;
            return (height + stripe / 8 - 1) / stripe;
        }
    }
}
