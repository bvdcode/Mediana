// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoColorProcessingTests
    {
        [Fact]
        public void RgbaResizeRetainsSourceDescriptionAndConversionProducesSrgbWithoutSourceDescription()
        {
            MediaBufferAllocator allocator = new(64 * 1024);
            CicpColorDescription description = new(1, 1, 0, true);
            using (VideoFrame input = CreateFrame(description, allocator))
            using (VideoFrame resized = NearestNeighborResize.Apply(input, 2, 2, allocator))
            using (VideoFrame converted = SrgbConverter.Apply(resized, allocator))
            using (VideoFrame expected = SrgbConverter.Apply(resized, description.Resolve(input.Format.Color), allocator))
            using (MediaBuffer png = PngEncoder.Encode(converted, allocator))
            {
                Assert.Same(description, resized.Format.ColorDescription);
                Assert.Equal(input.Format.Color, resized.Format.Color);
                Assert.Null(converted.Format.ColorDescription);
                Assert.Equal(VideoColorInfo.Srgb, converted.Format.Color);
                Assert.Equal(expected.Data.ToArray(), converted.Data.ToArray());
                Assert.NotEmpty(png.Memory.ToArray());
                Assert.Throws<NotSupportedException>(() => PngEncoder.Encode(input, allocator));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void IccDescriptionAllowsRawResizeButNeverAnImplicitSrgbInterpretation()
        {
            MediaBufferAllocator allocator = new(64 * 1024);
            IccColorDescription description = new([1, 2, 3], false);
            using (VideoFrame input = CreateFrame(description, allocator))
            using (VideoFrame resized = NearestNeighborResize.Apply(input, 2, 2, allocator))
            {
                Assert.Same(description, resized.Format.ColorDescription);
                Assert.Throws<NotSupportedException>(() => PngEncoder.Encode(resized, allocator));
                Assert.Throws<NotSupportedException>(() => SrgbConverter.Apply(resized, allocator));
                Assert.Throws<NotSupportedException>(() => SrgbPreview.Fit(resized, VideoDisplayMatrix.Identity, 2, 2, allocator));
                using VideoFrame explicitColor = SrgbConverter.Apply(resized, VideoColorInfo.Srgb, allocator);
                Assert.Null(explicitColor.Format.ColorDescription);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static VideoFrame CreateFrame(VideoColorDescription description, MediaBufferAllocator allocator)
        {
            VideoFormat format = new(1, 1, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square, description);
            using WritableMediaBuffer writable = allocator.Allocate(4);
            new byte[] { 50, 100, 150, 255 }.CopyTo(writable.Memory.Span);
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, format.CreateTightLayout());
        }
    }
}
