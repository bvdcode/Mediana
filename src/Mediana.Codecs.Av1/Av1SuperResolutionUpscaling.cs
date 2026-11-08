// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Av1
{
    internal static class Av1SuperResolutionUpscaling
    {
        internal static void Frame(ReadOnlySpan<byte> input, Av1SequenceHeader coded, Span<byte> output, Av1SequenceHeader display, CancellationToken token) =>
            FrameCore(input, coded, output, display, byte.MaxValue, token);

        internal static void Frame(ReadOnlySpan<ushort> input, Av1SequenceHeader coded, Span<ushort> output, Av1SequenceHeader display, int depth, CancellationToken token)
        {
            if (depth is not (10 or 12)) { throw new ArgumentOutOfRangeException(nameof(depth)); }
            FrameCore(input, coded, output, display, (1 << depth) - 1, token);
        }

        private static void FrameCore<T>(ReadOnlySpan<T> input, Av1SequenceHeader coded, Span<T> output, Av1SequenceHeader display, int maximum, CancellationToken token)
            where T : unmanaged, IBinaryInteger<T>
        {
            Validate(input.Length, coded, display);
            if (output.Length != display.SampleCount) { throw new ArgumentException("The upscaled frame extent is invalid.", nameof(output)); }
            if (input.Overlaps(output)) { throw new ArgumentException("Upscaled frames must use distinct storage.", nameof(output)); }
            for (int plane = 0; plane < (coded.Monochrome ? 1 : 3); plane++)
            {
                int shift = coded.PlaneShift(plane);
                int width = (coded.Width + shift) >> shift;
                int height = (coded.Height + shift) >> shift;
                int targetWidth = (display.Width + shift) >> shift;
                int stride = coded.PlaneStride(plane), targetStride = display.PlaneStride(plane);
                for (int row = 0; row < height; row++)
                {
                    token.ThrowIfCancellationRequested();
                    ReadOnlySpan<T> source = input.Slice(coded.PlaneOffset(plane) + row * stride, stride);
                    Span<T> target = output.Slice(display.PlaneOffset(plane) + row * targetStride, targetWidth);
                    Av1SuperResolutionFilter.RowCore(source, width, target, maximum);
                }
            }
        }

        internal static void SaveBoundaries(ReadOnlySpan<byte> input, Av1SequenceHeader coded, Av1SequenceHeader display,
            Av1RestorationParameters parameters, Span<byte> boundaries, CancellationToken token) => SaveBoundariesCore(input, coded, display, parameters, boundaries, byte.MaxValue, token);

        internal static void SaveBoundaries(ReadOnlySpan<ushort> input, Av1SequenceHeader coded, Av1SequenceHeader display,
            Av1RestorationParameters parameters, Span<ushort> boundaries, int depth, CancellationToken token)
        {
            if (depth is not (10 or 12)) { throw new ArgumentOutOfRangeException(nameof(depth)); }
            SaveBoundariesCore(input, coded, display, parameters, boundaries, (1 << depth) - 1, token);
        }

        private static void SaveBoundariesCore<T>(ReadOnlySpan<T> input, Av1SequenceHeader coded, Av1SequenceHeader display,
            Av1RestorationParameters parameters, Span<T> boundaries, int maximum, CancellationToken token) where T : unmanaged, IBinaryInteger<T>
        {
            Validate(input.Length, coded, display);
            if (boundaries.Length != Av1Restoration.BoundaryLength(display, parameters))
            {
                throw new ArgumentException("The upscaled restoration boundary extent is invalid.", nameof(boundaries));
            }
            if (input.Overlaps(boundaries)) { throw new ArgumentException("Restoration boundaries must use distinct storage.", nameof(boundaries)); }
            int offset = 0;
            for (int plane = 0; plane < (coded.Monochrome ? 1 : 3); plane++)
            {
                if (parameters.Type(plane) == Av1RestorationType.None) { continue; }
                int shift = coded.PlaneShift(plane);
                int width = (coded.Width + shift) >> shift;
                int height = (coded.Height + shift) >> shift;
                int targetWidth = (display.Width + shift) >> shift;
                int stride = coded.PlaneStride(plane), targetStride = display.PlaneStride(plane);
                int stripe = 64 >> shift;
                for (int boundary = 0; boundary < Av1Restoration.BoundaryCount(display, plane); boundary++)
                {
                    int row = (boundary + 1) * stripe - stripe / 8;
                    for (int line = -2; line < 2; line++)
                    {
                        token.ThrowIfCancellationRequested();
                        int sourceRow = Math.Min(row + line, height - 1);
                        ReadOnlySpan<T> source = input.Slice(coded.PlaneOffset(plane) + sourceRow * stride, stride);
                        Av1SuperResolutionFilter.RowCore(source, width, boundaries.Slice(offset, targetWidth), maximum);
                        offset += targetStride;
                    }
                }
            }
        }

        private static void Validate(int inputLength, Av1SequenceHeader coded, Av1SequenceHeader display)
        {
            if (inputLength != coded.SampleCount || display.Width < coded.Width || display.Height != coded.Height || display.Monochrome != coded.Monochrome)
            {
                throw new ArgumentException("The coded and upscaled frame extents are inconsistent.");
            }
        }
    }
}
