// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class FloatingRgbaColorTests
    {
        [Fact]
        public void PremultipliedLinearSamplesConvertWithAlphaAndClippingWithoutChangingSource()
        {
            MediaBufferAllocator allocator = new(1024);
            float[] samples = [.125f, .25f, .5f, .5f, -1, 0, 4, 1, 7, 2, 1, 0];
            VideoFormat format = new(3, 1, VideoPixelFormat.RgbaFloat32Premultiplied,
                VideoColorInfo.Srgb with { Transfer = TransferFunction.Linear }, SampleAspectRatio.Square);
            using (WritableMediaBuffer writable = allocator.Allocate(samples.Length * 4))
            {
                MemoryMarshal.AsBytes(samples.AsSpan()).CopyTo(writable.Span);
                using MediaBuffer pixels = writable.Publish();
                using VideoFrame frame = new(format, pixels, format.CreateTightLayout());
                using VideoFrame converted = SrgbConverter.Apply(frame, allocator);
                Assert.Equal(new byte[] { 137, 188, 255, 128, 0, 0, 255, 255, 0, 0, 0, 0 }, converted.Data.ToArray());
                Assert.Equal(samples, MemoryMarshal.Cast<byte, float>(frame.Data.Span).ToArray());
                Assert.ThrowsAny<OperationCanceledException>(() => SrgbConverter.Apply(frame, allocator, new(true)));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(ColorPrimaries.Bt709, .640, .330, .300, .600)]
        [InlineData(ColorPrimaries.DisplayP3, .680, .320, .265, .690)]
        public void ExplicitChromaticitiesMatchStandardColorSpace(ColorPrimaries primaries, double rx, double ry, double gx, double gy)
        {
            MediaBufferAllocator allocator = new(10_000);
            VideoColorInfo color = new(primaries, TransferFunction.Linear, MatrixCoefficients.Rgb, ColorRange.Full);
            RgbChromaticitiesColorDescription description = new(new(rx, ry, gx, gy, .150, .060, .3127, .3290));
            VideoFormat labeled = new(8, 1, VideoPixelFormat.Rgba32, color, SampleAspectRatio.Square);
            VideoFormat described = new(8, 1, VideoPixelFormat.Rgba32, color with { Primaries = ColorPrimaries.Unspecified }, SampleAspectRatio.Square, description);
            using WritableMediaBuffer output = allocator.Allocate(32);
            for (int i = 0; i < 8; i++)
            { output.Span[i * 4] = (byte)(i * 31); output.Span[i * 4 + 1] = (byte)(255 - i * 31); output.Span[i * 4 + 2] = 120; output.Span[i * 4 + 3] = 255; }
            using MediaBuffer pixels = output.Publish();
            using VideoFrame first = new(labeled, pixels, labeled.CreateTightLayout());
            using VideoFrame second = new(described, pixels, described.CreateTightLayout());
            using VideoFrame expected = SrgbConverter.Apply(first, allocator);
            using VideoFrame actual = SrgbConverter.ApplyWithColorPolicy(second, _ => throw new InvalidOperationException("Explicit chromaticities take precedence."), allocator);
            Assert.Equal(expected.Data.ToArray(), actual.Data.ToArray());
        }

        [Fact]
        public void InvalidChromaticityValuesDoNotCreateAColorDescription()
        {
            Assert.Throws<ArgumentException>(() => new RgbChromaticitiesColorDescription(new(double.NaN, .3, .2, .6, .1, .1, .3, .3)));
            Assert.Throws<ArgumentException>(() => new RgbChromaticitiesColorDescription(new(.6, 0, .2, .6, .1, .1, .3, .3)));
        }
    }
}
