// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Reusable progressive YUV420P8 area downscaling with explicit chroma siting and edge extension.
    /// Filters stored sample values, preserves color/timing and adjusts SAR to preserve display aspect.
    /// Borrows input frames and allocator; calls and disposal must not overlap.</summary>
    public class Yuv420AreaResizer : IDisposable
    {
        private const int RangeBytes = 2 * sizeof(int) + 2 * sizeof(long);
        private readonly VideoFormat inputFormat;
        private readonly MediaBufferAllocator allocator;
        private readonly WritableMediaBuffer ranges;
        private readonly WritableMediaBuffer? workspace;
        private bool disposed;
        private int busy;

        private Yuv420AreaResizer(VideoFormat inputFormat, VideoFormat outputFormat, MediaBufferAllocator allocator,
            WritableMediaBuffer ranges, WritableMediaBuffer? workspace)
        {
            this.inputFormat = inputFormat;
            OutputFormat = outputFormat;
            this.allocator = allocator;
            this.ranges = ranges;
            this.workspace = workspace;
        }

        public VideoFormat OutputFormat { get; }

        public static Yuv420AreaResizer Create(VideoFormat inputFormat, int width, int height, MediaBufferAllocator allocator,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputFormat);
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);
            if (inputFormat.PixelFormat != VideoPixelFormat.Yuv420P8 ||
                ((width | height) & 1) != 0 ||
                width > inputFormat.Width || height > inputFormat.Height)
            {
                throw new NotSupportedException("Area video resizing requires YUV420P8 input and even-sized output without enlargement.");
            }
            (int horizontal, int vertical) = ChromaSamplePosition.InHalfLumaSamples(inputFormat.Color.ChromaLocation);
            SampleAspectRatio? aspect = ResizeGeometry.PreserveDisplayAspect(inputFormat, width, height);
            VideoFormat outputFormat = new(width, height, VideoPixelFormat.Yuv420P8, inputFormat.Color, aspect, inputFormat.ColorDescription);
            WritableMediaBuffer storage = allocator.Allocate(checked((width + height + width / 2 + height / 2) * RangeBytes));
            bool succeeded = false;
            WritableMediaBuffer? workspace = null;
            try
            {
                Span<AreaSampleRange> all = MemoryMarshal.Cast<byte, AreaSampleRange>(storage.Memory.Span);
                int offset = 0;
                AreaSampleRange.Fill(all.Slice(offset, width), inputFormat.Width, 2, cancellationToken);
                offset += width;
                AreaSampleRange.Fill(all.Slice(offset, height), inputFormat.Height, 2, cancellationToken);
                offset += height;
                AreaSampleRange.Fill(all.Slice(offset, width / 2), (inputFormat.Width + 1) / 2, inputFormat.Width, 2, horizontal + 1, cancellationToken);
                offset += width / 2;
                AreaSampleRange.Fill(all.Slice(offset, height / 2), (inputFormat.Height + 1) / 2, inputFormat.Height, 2, vertical + 1, cancellationToken);
                if (width != inputFormat.Width || height != inputFormat.Height)
                {
                    workspace = allocator.Allocate(checked(2 * width * sizeof(long)));
                }
                Yuv420AreaResizer result = new(inputFormat, outputFormat, allocator, storage, workspace);
                succeeded = true;
                return result;
            }
            finally
            {
                if (!succeeded)
                {
                    storage.Dispose();
                    workspace?.Dispose();
                }
            }
        }

        /// <summary>Fits storage dimensions into a box without enlargement, rounding down to even dimensions.
        /// Output SAR compensates rounding; unknown SAR remains unknown.</summary>
        public static Yuv420AreaResizer CreateFit(VideoFormat inputFormat, int maximumWidth, int maximumHeight,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(inputFormat);
            (int width, int height) = ResizeGeometry.FitEven(inputFormat, maximumWidth, maximumHeight);
            return Create(inputFormat, width, height, allocator, cancellationToken);
        }

        /// <summary>Resizes into independent tightly packed storage charged to the configured allocator.</summary>
        public VideoFrame Apply(VideoFrame input, CancellationToken cancellationToken = default)
            => ApplyCore(input, false, cancellationToken);

        /// <summary>Resizes or retains an independently disposable frame when dimensions are unchanged.
        /// Retention preserves the input planes and their original allocation budget.</summary>
        public VideoFrame ResizeOrRetain(VideoFrame input, CancellationToken cancellationToken = default)
            => ApplyCore(input, true, cancellationToken);

        private VideoFrame ApplyCore(VideoFrame input, bool retainIdentity, CancellationToken cancellationToken)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ArgumentNullException.ThrowIfNull(input);
                cancellationToken.ThrowIfCancellationRequested();
                if (input.Format.Width != inputFormat.Width || input.Format.Height != inputFormat.Height ||
                    input.Format.PixelFormat != inputFormat.PixelFormat || input.Format.Color != inputFormat.Color ||
                    !Equals(input.Format.ColorDescription, inputFormat.ColorDescription) ||
                    input.Format.SampleAspectRatio != inputFormat.SampleAspectRatio)
                {
                    throw new NotSupportedException("The input frame differs from the video resizer configuration.");
                }
                if (retainIdentity && inputFormat.Width == OutputFormat.Width && inputFormat.Height == OutputFormat.Height)
                {
                    return input.Retain();
                }
                PlaneLayout[] planes = OutputFormat.CreateTightLayout();
                using WritableMediaBuffer output = allocator.Allocate(checked((int)planes[^1].EndOffset));
                if (workspace is null)
                {
                    for (int plane = 0; plane < planes.Length; plane++)
                    {
                        for (int y = 0; y < planes[plane].Height; y++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            input.GetRow(plane, y).CopyTo(output.Memory.Span.Slice(planes[plane].Offset + y * planes[plane].Stride));
                        }
                    }
                    using MediaBuffer copy = output.Publish();
                    return new VideoFrame(OutputFormat, copy, planes, input.Timestamp, input.Duration);
                }
                ReadOnlySpan<AreaSampleRange> all = MemoryMarshal.Cast<byte, AreaSampleRange>(ranges.Memory.Span);
                int offset = 0;
                ReadOnlySpan<AreaSampleRange> lumaX = all.Slice(offset, OutputFormat.Width);
                offset += OutputFormat.Width;
                ReadOnlySpan<AreaSampleRange> lumaY = all.Slice(offset, OutputFormat.Height);
                offset += OutputFormat.Height;
                ReadOnlySpan<AreaSampleRange> chromaX = all.Slice(offset, OutputFormat.Width / 2);
                offset += OutputFormat.Width / 2;
                ReadOnlySpan<AreaSampleRange> chromaY = all[offset..];
                for (int plane = 0; plane < 3; plane++)
                {
                    Render(input, plane, output.Memory.Span.Slice(planes[plane].Offset), planes[plane].Stride,
                        plane == 0 ? lumaX : chromaX, plane == 0 ? lumaY : chromaY,
                        MemoryMarshal.Cast<byte, long>(workspace.Memory.Span), cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();
                using MediaBuffer buffer = output.Publish();
                return new VideoFrame(OutputFormat, buffer, planes, input.Timestamp, input.Duration);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        private static void Render(VideoFrame input, int plane, Span<byte> output, int stride,
            ReadOnlySpan<AreaSampleRange> horizontal, ReadOnlySpan<AreaSampleRange> vertical,
            Span<long> workspace, CancellationToken cancellationToken)
        {
            long denominator = 16L * input.Format.Width * input.Format.Height;
            int extentDenominator = plane == 0 ? 1 : 2;
            Span<long> filtered = workspace[..horizontal.Length];
            Span<long> sums = workspace.Slice(horizontal.Length, horizontal.Length);
            int cachedRow = -1;
            for (int y = 0; y < vertical.Length; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AreaSampleRange rows = vertical[y];
                sums.Clear();
                for (int sourceY = rows.First; sourceY <= rows.Last; sourceY++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (sourceY != cachedRow)
                    {
                        FilterRow(input.GetRow(plane, sourceY), horizontal, filtered, extentDenominator, cancellationToken);
                        cachedRow = sourceY;
                    }
                    long weight = rows.Weight(sourceY, 4L * vertical.Length * extentDenominator);
                    for (int x = 0; x < sums.Length; x++)
                    {
                        sums[x] += filtered[x] * weight;
                    }
                }
                Span<byte> outputRow = output.Slice(y * stride, horizontal.Length);
                for (int x = 0; x < sums.Length; x++)
                {
                    outputRow[x] = (byte)((sums[x] + denominator / 2) / denominator);
                }
            }
        }

        private static void FilterRow(ReadOnlySpan<byte> input, ReadOnlySpan<AreaSampleRange> horizontal,
            Span<long> output, int extentDenominator, CancellationToken cancellationToken)
        {
            long interiorWeight = 4L * horizontal.Length * extentDenominator;
            for (int x = 0; x < horizontal.Length; x++)
            {
                if ((x & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                AreaSampleRange columns = horizontal[x];
                long sum = input[columns.First] * columns.FirstWeight;
                if (columns.Last > columns.First)
                {
                    sum += input[columns.Last] * columns.LastWeight;
                    long interior = 0;
                    for (int sourceX = columns.First + 1; sourceX < columns.Last; sourceX++)
                    {
                        if ((sourceX & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                        interior += input[sourceX];
                    }
                    sum += interior * interiorWeight;
                }
                output[x] = sum;
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("Concurrent video resizer operations are not supported.");
            }
        }

        public void Dispose()
        {
            Enter();
            try
            {
                disposed = true;
                ranges.Dispose();
                workspace?.Dispose();
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }
    }
}
