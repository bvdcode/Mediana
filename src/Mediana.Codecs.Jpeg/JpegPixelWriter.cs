// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Color;

namespace Mediana.Codecs.Jpeg
{
    internal static class JpegPixelWriter
    {
        internal static VideoFrame Create(JpegDocument image, MediaBufferAllocator allocator, CancellationToken token)
        {
            bool deviceCmyk = image.Components.Count == 4;
            bool preserveCmyk = deviceCmyk && image.Metadata.HasIcc;
            VideoColorInfo color = !deviceCmyk && image.Metadata.IsSrgb
                ? VideoColorInfo.Srgb : new(Matrix: MatrixCoefficients.Rgb, Range: ColorRange.Full);
            VideoColorDescription? description = null;
            if (image.Metadata.HasIcc)
            {
                description = image.Metadata.CreateIccDescription();
            }
            else if (!deviceCmyk && image.Metadata.Uncalibrated) { description = new JpegExifColorDescription(); }
            if (preserveCmyk) { color = new(Range: ColorRange.Full); }
            bool swap = image.Metadata.Orientation.SwapsAxes;
            JpegSampleLayout samples = image.Samples;
            VideoFormat format = new(swap ? samples.Height : samples.Width, swap ? samples.Width : samples.Height,
                preserveCmyk ? VideoPixelFormat.Cmyk32 : VideoPixelFormat.Rgba32, color, image.Metadata.Aspect, description);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer output = allocator.Allocate(checked((int)layout[0].EndOffset));
            Write(samples, output.Memory.Span, image.Metadata.ColorSpace, token, image.Metadata.Orientation, image.Metadata.Centered, preserveCmyk);
            using MediaBuffer buffer = output.Publish();
            return new VideoFrame(format, buffer, layout);
        }

        internal static void Write(in JpegSampleLayout image, Span<byte> pixels, JpegColorSpace color, CancellationToken token) =>
            Write(image, pixels, color, token, OrthogonalTransform.Identity, true);

        private static void Write(in JpegSampleLayout image, Span<byte> pixels, JpegColorSpace color, CancellationToken token,
            OrthogonalTransform orientation, bool centered, bool preserveCmyk = false)
        {
            if (pixels.Length < (long)image.Width * image.Height * 4)
            {
                throw new ArgumentException("The JPEG pixel destination is too short.", nameof(pixels));
            }
            token.ThrowIfCancellationRequested();
            int width = orientation.SwapsAxes ? image.Height : image.Width;
            int height = orientation.SwapsAxes ? image.Width : image.Height;
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                int sourceX = orientation.SourceX(0, y, image.Width);
                int sourceY = orientation.SourceY(0, y, image.Height);
                JpegSampleCursor firstSamples = new(image, image.Components[0], sourceX, sourceY, orientation, centered);
                JpegSampleCursor secondSamples = default;
                JpegSampleCursor thirdSamples = default;
                JpegSampleCursor blackSamples = default;
                if (image.Components.Count > 1)
                {
                    secondSamples = new(image, image.Components[1], sourceX, sourceY, orientation, centered);
                    thirdSamples = new(image, image.Components[2], sourceX, sourceY, orientation, centered);
                }
                if (image.Components.Count == 4)
                {
                    blackSamples = new(image, image.Components[3], sourceX, sourceY, orientation, centered);
                }
                for (int x = 0; x < width; x++)
                {
                    int first = firstSamples.ReadNext();
                    Span<byte> pixel = pixels.Slice((y * width + x) * 4, 4);
                    if (image.Components.Count == 1)
                    {
                        pixel[0] = (byte)first;
                        pixel[1] = (byte)first;
                        pixel[2] = (byte)first;
                    }
                    else
                    {
                        int second = secondSamples.ReadNext();
                        int third = thirdSamples.ReadNext();
                        if (color == JpegColorSpace.Rgb)
                        {
                            pixel[0] = (byte)first;
                            pixel[1] = (byte)second;
                            pixel[2] = (byte)third;
                        }
                        else if (color is JpegColorSpace.YCbCr or JpegColorSpace.Ycck)
                        {
                            int cb = second - 128;
                            int cr = third - 128;
                            pixel[0] = Clamp(first + ((91881 * cr + 32768) >> 16));
                            pixel[1] = Clamp(first - ((22554 * cb + 46802 * cr + 32768) >> 16));
                            pixel[2] = Clamp(first + ((116130 * cb + 32768) >> 16));
                        }
                        else if (color is JpegColorSpace.Cmyk or JpegColorSpace.InvertedCmyk)
                        {
                            pixel[0] = (byte)first;
                            pixel[1] = (byte)second;
                            pixel[2] = (byte)third;
                        }
                        else { throw new ArgumentOutOfRangeException(nameof(color)); }
                        if (image.Components.Count == 4)
                        {
                            int black = blackSamples.ReadNext();
                            switch (color)
                            {
                                case JpegColorSpace.Cmyk:
                                    break;
                                case JpegColorSpace.InvertedCmyk:
                                    pixel[0] = (byte)(255 - pixel[0]);
                                    pixel[1] = (byte)(255 - pixel[1]);
                                    pixel[2] = (byte)(255 - pixel[2]);
                                    black = 255 - black;
                                    break;
                                case JpegColorSpace.Ycck:
                                    black = 255 - black;
                                    break;
                                case JpegColorSpace.Rgb:
                                case JpegColorSpace.YCbCr:
                                default:
                                    throw new ArgumentOutOfRangeException(nameof(color));
                            }
                            if (preserveCmyk) { pixel[3] = (byte)black; }
                            else { (pixel[0], pixel[1], pixel[2]) = DeviceCmyk.ToRgb(pixel[0], pixel[1], pixel[2], (byte)black); }
                        }
                    }
                    if (!preserveCmyk) { pixel[3] = 255; }
                }
            }
            token.ThrowIfCancellationRequested();
        }

        private static byte Clamp(int value) => (byte)Math.Clamp(value, 0, 255);
    }
}
