// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Processing
{
    /// <summary>Separable RGBA32 resampling with scale-adjusted antialiasing and premultiplied-alpha filtering.
    /// Borrows input, returns an independent lease and retains color, sample aspect ratio and timing.
    /// Output, weights and the rolling row cache count against the allocator. No full intermediate image is created.</summary>
    public static class RgbaResize
    {
        public static VideoFrame Apply(VideoFrame input, int width, int height, MediaBufferAllocator allocator,
            RgbaResizeOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
            cancellationToken.ThrowIfCancellationRequested();
            options ??= new();
            if (input.Format.PixelFormat != VideoPixelFormat.Rgba32)
            {
                throw new NotSupportedException("Image resizing requires straight RGBA32 pixels.");
            }
            bool linear = options.ColorSpace switch
            {
                ResizeColorSpace.LinearSrgb => true,
                ResizeColorSpace.Encoded => false,
                _ => throw new ArgumentOutOfRangeException(nameof(options))
            };
            switch (options.Filter)
            {
                case ImageResizeFilter.NearestNeighbor:
                    return NearestNeighborResize.Apply(input, width, height, allocator, cancellationToken);
                case ImageResizeFilter.Bilinear:
                case ImageResizeFilter.Bicubic:
                case ImageResizeFilter.Lanczos3:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(options));
            }
            if (width == input.Format.Width && height == input.Format.Height) { return input.Retain(); }
            if (linear && (input.Format.ColorDescription is not null || input.Format.Color != VideoColorInfo.Srgb))
            {
                throw new NotSupportedException("Linear-light resizing requires sRGB pixels. Convert to sRGB first or explicitly select encoded-channel filtering.");
            }
            VideoScanMode scanMode = VideoScanGeometry.Map(input.Format.ScanMode,
                new(input.Format.Width, input.Format.Height, width, height, OrthogonalTransform.Identity));
            using ResizeAxis horizontal = new(input.Format.Width, width, options.Filter, allocator, cancellationToken);
            using ResizeAxis vertical = new(input.Format.Height, height, options.Filter, allocator, cancellationToken);
            using WritableMediaBuffer rows = allocator.Allocate(checked(width * vertical.Capacity * 16));
            using WritableMediaBuffer scratch = allocator.Allocate(checked((input.Format.Width + width) * 16));
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, input.Format.Color,
                input.Format.SampleAspectRatio, input.Format.ColorDescription, scanMode, VideoScanGeometry.FullFrameHeight(input.Format, height));
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[0].EndOffset));
            Span<Vector4> cache = MemoryMarshal.Cast<byte, Vector4>(rows.Memory.Span);
            Span<Vector4> decoded = MemoryMarshal.Cast<byte, Vector4>(scratch.Memory.Span)[..input.Format.Width];
            Span<Vector4> accumulated = MemoryMarshal.Cast<byte, Vector4>(scratch.Memory.Span)[input.Format.Width..];
            int lastRow = -1;
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int first = vertical.Starts[y];
                ReadOnlySpan<float> weights = vertical.Weights(y);
                for (int row = Math.Max(lastRow + 1, first); row < first + weights.Length; row++)
                {
                    DecodeRow(input.GetRow(0, row), decoded, linear, cancellationToken);
                    FilterRow(decoded, horizontal, cache.Slice(row % vertical.Capacity * width, width), cancellationToken);
                    lastRow = row;
                }
                accumulated.Clear();
                for (int i = 0; i < weights.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReadOnlySpan<Vector4> row = cache.Slice((first + i) % vertical.Capacity * width, width);
                    float weight = weights[i];
                    for (int x = 0; x < width; x++)
                    {
                        if ((x & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                        accumulated[x] += row[x] * weight;
                    }
                }
                Span<byte> destination = writable.Memory.Span.Slice(y * layout[0].Stride, layout[0].RowBytes);
                for (int x = 0; x < width; x++)
                {
                    if ((x & 255) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                    Write(accumulated[x], destination.Slice(x * 4, 4), linear);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, layout, input.Timestamp, input.Duration, input.Presentation);
        }

        private static void DecodeRow(ReadOnlySpan<byte> input, Span<Vector4> output, bool linear, CancellationToken token)
        {
            for (int x = 0; x < output.Length; x++)
            {
                if ((x & 255) == 0) { token.ThrowIfCancellationRequested(); }
                int offset = x * 4;
                float alpha = input[offset + 3] / 255f;
                output[x] = new Vector4(ResizeTransfer.Decode(input[offset], linear), ResizeTransfer.Decode(input[offset + 1], linear),
                    ResizeTransfer.Decode(input[offset + 2], linear), 1) * alpha;
            }
        }

        private static void FilterRow(ReadOnlySpan<Vector4> input, ResizeAxis axis, Span<Vector4> output, CancellationToken token)
        {
            for (int x = 0; x < output.Length; x++)
            {
                if ((x & 255) == 0) { token.ThrowIfCancellationRequested(); }
                Vector4 sum = Vector4.Zero;
                int first = axis.Starts[x];
                ReadOnlySpan<float> weights = axis.Weights(x);
                for (int i = 0; i < weights.Length; i++)
                {
                    if ((i & 255) == 0) { token.ThrowIfCancellationRequested(); }
                    sum += input[first + i] * weights[i];
                }
                output[x] = sum;
            }
        }

        private static void Write(Vector4 pixel, Span<byte> output, bool linear)
        {
            byte alpha = ResizeTransfer.Byte(pixel.W);
            if (alpha == 0) { output.Clear(); return; }
            output[0] = ResizeTransfer.Encode(pixel.X / pixel.W, linear);
            output[1] = ResizeTransfer.Encode(pixel.Y / pixel.W, linear);
            output[2] = ResizeTransfer.Encode(pixel.Z / pixel.W, linear);
            output[3] = alpha;
        }
    }
}
