// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats.Color;
using Mediana.Processing;

namespace Mediana.Codecs.Raster.Tests
{
    public class StandardIccProfileTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StandardProfilesPreserveSrgbSamplesAndAlpha(bool gray)
        {
            int width = gray ? 256 : 17 * 17 * 17;
            MediaBufferAllocator allocator = new(width * 8);
            using WritableMediaBuffer writable = allocator.Allocate(width * 4);
            for (int i = 0; i < width; i++)
            {
                writable.Memory.Span[i * 4] = (byte)(gray ? i : i % 17 * 15);
                writable.Memory.Span[i * 4 + 1] = (byte)(gray ? i : i / 17 % 17 * 15);
                writable.Memory.Span[i * 4 + 2] = (byte)(gray ? i : i / 289 * 15);
                writable.Memory.Span[i * 4 + 3] = (byte)i;
            }
            using MediaBuffer storage = writable.Publish();
            IccColorDescription profile = new(gray ? IccProfiles.SrgbGray : IccProfiles.Srgb, false);
            VideoFormat format = new(width, 1, VideoPixelFormat.Rgba32,
                new(Matrix: MatrixCoefficients.Rgb, Range: ColorRange.Full), SampleAspectRatio.Square, profile);
            using VideoFrame input = new(format, storage, format.CreateTightLayout());
            using VideoFrame converted = SrgbConverter.Apply(input, allocator);
            for (int i = 0; i < width * 4; i++)
            {
                Assert.InRange(Math.Abs(input.Data.Span[i] - converted.Data.Span[i]), 0, i % 4 == 3 ? 0 : 1);
            }
        }
    }
}
