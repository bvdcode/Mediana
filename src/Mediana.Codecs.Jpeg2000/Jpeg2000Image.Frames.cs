// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Codecs.Jpeg2000
{
    public partial class Jpeg2000Image
    {
        /// <summary>Converts gray, sRGB or sYCC components to owned straight sRGB RGBA8.
        /// Native precision remains available through the component API. Raw codestreams require an explicit
        /// color space; ICC profiles are unsupported. The allocator counts only the converted output.</summary>
        public VideoFrame ToVideoFrame(MediaBufferAllocator allocator, SampleAspectRatio sampleAspectRatio,
            Jpeg2000ColorSpace? sourceColorSpace = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            cancellationToken.ThrowIfCancellationRequested();
            if (HasIccProfile)
            {
                throw new NotSupportedException("JPEG 2000 ICC profiles require an explicit color transform.");
            }
            Jpeg2000ColorSpace colorSpace = sourceColorSpace ?? ColorSpace;
            int colors = colorSpace switch
            {
                Jpeg2000ColorSpace.Gray => 1,
                Jpeg2000ColorSpace.Srgb or Jpeg2000ColorSpace.Sycc => 3,
                Jpeg2000ColorSpace.Unspecified => throw new NotSupportedException("The JPEG 2000 color space is unspecified. Supply a known interpretation explicitly."),
                _ => throw new ArgumentOutOfRangeException(nameof(sourceColorSpace))
            };
            int expected = colors;
            if (AlphaComponent >= 0)
            {
                expected++;
            }
            if (ComponentCount != expected || AlphaComponent >= ComponentCount)
            {
                throw new NotSupportedException("JPEG 2000 component layout cannot be represented as gray or RGB with optional alpha.");
            }
            Span<int> channels = stackalloc int[3];
            int channel = 0;
            for (int c = 0; c < ComponentCount; c++)
            {
                if (c != AlphaComponent)
                {
                    channels[channel++] = c;
                }
            }
            VideoFormat format = new(Width, Height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, sampleAspectRatio);
            PlaneLayout[] layout = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)layout[0].EndOffset));
            Span<byte> output = writable.Memory.Span;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if ((x & 255) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    WriteRgbaPixel(colorSpace, channels, x, y, output.Slice((y * Width + x) * 4, 4));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, layout);
        }

        private void WriteRgbaPixel(Jpeg2000ColorSpace colorSpace, ReadOnlySpan<int> channels, int x, int y, Span<byte> output)
        {
            double r = NormalizedSample(x, y, channels[0]);
            double g;
            double b;
            switch (colorSpace)
            {
                case Jpeg2000ColorSpace.Gray:
                    g = b = r;
                    break;
                case Jpeg2000ColorSpace.Srgb:
                    g = NormalizedSample(x, y, channels[1]);
                    b = NormalizedSample(x, y, channels[2]);
                    break;
                case Jpeg2000ColorSpace.Sycc:
                    double cb = ChromaSample(x, y, channels[1]);
                    double cr = ChromaSample(x, y, channels[2]);
                    g = r - 0.344136 * cb - 0.714136 * cr;
                    b = r + 1.772 * cb;
                    r += 1.402 * cr;
                    break;
                case Jpeg2000ColorSpace.Unspecified:
                default:
                    throw new NotSupportedException("Unsupported JPEG 2000 color space.");
            }
            double opacity = 1;
            if (AlphaComponent >= 0)
            {
                opacity = NormalizedSample(x, y, AlphaComponent);
            }
            if (PremultipliedAlpha)
            {
                if (opacity > 0)
                {
                    r /= opacity;
                    g /= opacity;
                    b /= opacity;
                }
                else
                {
                    r = g = b = 0;
                }
            }
            output[0] = ToByte(r);
            output[1] = ToByte(g);
            output[2] = ToByte(b);
            output[3] = ToByte(opacity);
        }

        private double NormalizedSample(int x, int y, int channel)
        {
            Jpeg2000Component component = GetComponent(channel);
            int sample = GetSample(x, y, channel);
            if (component.IsSigned)
            {
                sample += 1 << (component.BitDepth - 1);
            }
            return sample / ((1 << component.BitDepth) - 1.0);
        }

        private double ChromaSample(int x, int y, int channel)
        {
            int bits = GetComponent(channel).BitDepth;
            return NormalizedSample(x, y, channel) - (1 << (bits - 1)) / ((1 << bits) - 1.0);
        }

        private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
    }
}
