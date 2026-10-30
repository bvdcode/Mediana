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
        private bool disposed;
        private int busy;

        private Yuv420AreaResizer(VideoFormat inputFormat, VideoFormat outputFormat, MediaBufferAllocator allocator,
            WritableMediaBuffer ranges)
        {
            this.inputFormat = inputFormat;
            OutputFormat = outputFormat;
            this.allocator = allocator;
            this.ranges = ranges;
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
                ((inputFormat.Width | inputFormat.Height | width | height) & 1) != 0 ||
                width > inputFormat.Width || height > inputFormat.Height)
            {
                throw new NotSupportedException("Area video resizing requires even-sized YUV420P8 input and output without enlargement.");
            }
            (int horizontal, int vertical) = ChromaSamplePosition.InHalfLumaSamples(inputFormat.Color.ChromaLocation);
            SampleAspectRatio? aspect = ResizeGeometry.PreserveDisplayAspect(inputFormat, width, height);
            VideoFormat outputFormat = new(width, height, VideoPixelFormat.Yuv420P8, inputFormat.Color, aspect);
            WritableMediaBuffer storage = allocator.Allocate(checked((width + height + width / 2 + height / 2) * RangeBytes));
            bool succeeded = false;
            try
            {
                Span<AreaSampleRange> all = MemoryMarshal.Cast<byte, AreaSampleRange>(storage.Memory.Span);
                int offset = 0;
                AreaSampleRange.Fill(all.Slice(offset, width), inputFormat.Width, 2, cancellationToken);
                offset += width;
                AreaSampleRange.Fill(all.Slice(offset, height), inputFormat.Height, 2, cancellationToken);
                offset += height;
                AreaSampleRange.Fill(all.Slice(offset, width / 2), inputFormat.Width / 2, horizontal + 1, cancellationToken);
                offset += width / 2;
                AreaSampleRange.Fill(all.Slice(offset, height / 2), inputFormat.Height / 2, vertical + 1, cancellationToken);
                Yuv420AreaResizer result = new(inputFormat, outputFormat, allocator, storage);
                succeeded = true;
                return result;
            }
            finally
            {
                if (!succeeded)
                {
                    storage.Dispose();
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

        public VideoFrame Apply(VideoFrame input, CancellationToken cancellationToken = default)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                ArgumentNullException.ThrowIfNull(input);
                cancellationToken.ThrowIfCancellationRequested();
                if (input.Format.Width != inputFormat.Width || input.Format.Height != inputFormat.Height ||
                    input.Format.PixelFormat != inputFormat.PixelFormat || input.Format.Color != inputFormat.Color ||
                    input.Format.SampleAspectRatio != inputFormat.SampleAspectRatio)
                {
                    throw new NotSupportedException("The input frame differs from the video resizer configuration.");
                }
                PlaneLayout[] planes = OutputFormat.CreateTightLayout();
                using WritableMediaBuffer output = allocator.Allocate(checked((int)planes[^1].EndOffset));
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
                        plane == 0 ? lumaX : chromaX, plane == 0 ? lumaY : chromaY, cancellationToken);
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
            ReadOnlySpan<AreaSampleRange> horizontal, ReadOnlySpan<AreaSampleRange> vertical, CancellationToken cancellationToken)
        {
            long denominator = 16L * input.Planes[plane].RowBytes * input.Planes[plane].Height;
            for (int y = 0; y < vertical.Length; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AreaSampleRange rows = vertical[y];
                for (int x = 0; x < horizontal.Length; x++)
                {
                    if ((x & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    AreaSampleRange columns = horizontal[x];
                    long sum = 0;
                    for (int sourceY = rows.First; sourceY <= rows.Last; sourceY++)
                    {
                        ReadOnlySpan<byte> row = input.GetRow(plane, sourceY);
                        long weightY = rows.Weight(sourceY, 4L * vertical.Length);
                        for (int sourceX = columns.First; sourceX <= columns.Last; sourceX++)
                        {
                            if ((sourceX & 1023) == 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                            sum += row[sourceX] * columns.Weight(sourceX, 4L * horizontal.Length) * weightY;
                        }
                    }
                    output[y * stride + x] = (byte)((sum + denominator / 2) / denominator);
                }
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
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }
    }
}
