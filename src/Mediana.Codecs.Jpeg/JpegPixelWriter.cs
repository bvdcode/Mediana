// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegPixelWriter
    {
        internal static VideoFrame Create(JpegDocument image, MediaBufferAllocator allocator, CancellationToken token)
        {
            VideoColorInfo color = new(Matrix: MatrixCoefficients.Rgb, Range: ColorRange.Full);
            JpegColorDescription? description = null;
            if (image.Metadata.HasIcc)
            {
                description = new();
            }
            VideoFormat format = new(image.Width, image.Height, VideoPixelFormat.Rgba32, color, image.Metadata.Aspect, description);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer output = allocator.Allocate(checked((int)layout[0].EndOffset));
            Write(new(image.Width, image.Height, image.MaximumHorizontal, image.MaximumVertical, image.Components),
                output.Memory.Span, image.Metadata.IsRgb, token);
            using MediaBuffer buffer = output.Publish();
            return new VideoFrame(format, buffer, layout);
        }

        internal static void Write(in JpegSampleLayout image, Span<byte> pixels, bool rgb, CancellationToken token)
        {
            if (pixels.Length < (long)image.Width * image.Height * 4)
            {
                throw new ArgumentException("The JPEG RGBA destination is too short.", nameof(pixels));
            }
            token.ThrowIfCancellationRequested();
            for (int y = 0; y < image.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < image.Width; x++)
                {
                    int first = Sample(image, image.Components[0], x, y);
                    Span<byte> pixel = pixels.Slice((y * image.Width + x) * 4, 4);
                    if (image.Components.Count == 1)
                    {
                        pixel[0] = (byte)first;
                        pixel[1] = (byte)first;
                        pixel[2] = (byte)first;
                    }
                    else
                    {
                        int second = Sample(image, image.Components[1], x, y);
                        int third = Sample(image, image.Components[2], x, y);
                        if (rgb)
                        {
                            pixel[0] = (byte)first;
                            pixel[1] = (byte)second;
                            pixel[2] = (byte)third;
                        }
                        else
                        {
                            int cb = second - 128;
                            int cr = third - 128;
                            pixel[0] = Clamp(first + ((91881 * cr + 32768) >> 16));
                            pixel[1] = Clamp(first - ((22554 * cb + 46802 * cr + 32768) >> 16));
                            pixel[2] = Clamp(first + ((116130 * cb + 32768) >> 16));
                        }
                    }
                    pixel[3] = 255;
                }
            }
            token.ThrowIfCancellationRequested();
        }

        private static int Sample(in JpegSampleLayout image, JpegComponent component, int x, int y)
        {
            ReadOnlySpan<byte> samples = component.Samples!.Memory.Span;
            if (component.Horizontal == image.MaximumHorizontal && component.Vertical == image.MaximumVertical)
            {
                return samples[y * component.Stride + x];
            }
            int dx = image.MaximumHorizontal * 2;
            int dy = image.MaximumVertical * 2;
            int nx = (2 * x + 1) * component.Horizontal - image.MaximumHorizontal;
            int ny = (2 * y + 1) * component.Vertical - image.MaximumVertical;
            int lowX = Math.DivRem(nx, dx, out int fractionX);
            int lowY = Math.DivRem(ny, dy, out int fractionY);
            if (fractionX < 0)
            {
                lowX--;
                fractionX += dx;
            }
            if (fractionY < 0)
            {
                lowY--;
                fractionY += dy;
            }
            int x0 = Math.Clamp(lowX, 0, component.Width - 1);
            int x1 = Math.Clamp(lowX + 1, 0, component.Width - 1);
            int y0 = Math.Clamp(lowY, 0, component.Height - 1) * component.Stride;
            int y1 = Math.Clamp(lowY + 1, 0, component.Height - 1) * component.Stride;
            int top = samples[y0 + x0] * (dx - fractionX) + samples[y0 + x1] * fractionX;
            int bottom = samples[y1 + x0] * (dx - fractionX) + samples[y1 + x1] * fractionX;
            int divisor = dx * dy;
            return (top * (dy - fractionY) + bottom * fractionY + divisor / 2) / divisor;
        }

        private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
    }
}
