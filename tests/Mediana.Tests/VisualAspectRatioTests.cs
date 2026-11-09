// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class VisualAspectRatioTests
    {
        [Fact]
        public async Task UndeclaredAspectRatioRequiresAnExplicitInterpretationAndRetainsSourcePixels()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (MediaBuffer buffer = Copy(allocator, [255, 0, 0, 255, 0, 0, 255, 255]))
            {
                VideoFormat format = new(2, 1, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb);
                using VideoFrame frame = new(format, buffer, format.CreateTightLayout());
                using MemoryStream encoded = new();
                await Assert.ThrowsAsync<NotSupportedException>(() => MediaEngine.ExportImageAsync(frame, encoded, new PngImageEncoder(),
                    allocator, VideoDisplayMatrix.Identity));
                List<string> warnings = [];
                VisualExportOptions options = new() { UnspecifiedSampleAspectRatio = SampleAspectRatio.Square, WarningHandler = warnings.Add };
                await MediaEngine.ExportImageAsync(frame, encoded, new PngImageEncoder(), allocator, VideoDisplayMatrix.Identity, options: options);
                using VideoFrame output = await new PngImageDecoder().DecodeAsync(encoded.ToArray(), new(), allocator);
                Assert.Equal(frame.Data.ToArray(), output.Data.ToArray());
                Assert.Equal(SampleAspectRatio.Square, output.Format.SampleAspectRatio);
                Assert.Null(frame.Format.SampleAspectRatio);
                Assert.Contains("1:1", Assert.Single(warnings), StringComparison.Ordinal);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task DeclaredAspectRatioAlwaysTakesPrecedenceOverTheUnspecifiedValuePolicy()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (MediaBuffer buffer = Copy(allocator, new byte[16]))
            {
                VideoFormat format = new(2, 2, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, new(1, 2));
                using VideoFrame frame = new(format, buffer, format.CreateTightLayout());
                List<string> warnings = [];
                VisualExportOptions options = new() { UnspecifiedSampleAspectRatio = SampleAspectRatio.Square, WarningHandler = warnings.Add };
                using MemoryStream encoded = new();
                await MediaEngine.ExportImageAsync(frame, encoded, new PngImageEncoder(), allocator, VideoDisplayMatrix.Identity, options: options);
                using VideoFrame output = await new PngImageDecoder().DecodeAsync(encoded.ToArray(), new(), allocator);
                Assert.Equal(1, output.Format.Width);
                Assert.Equal(2, output.Format.Height);
                Assert.Empty(warnings);
                Assert.Equal(new SampleAspectRatio(1, 2), frame.Format.SampleAspectRatio);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InvalidExplicitAspectRatioIsRejectedBeforeExport()
        {
            VisualExportOptions options = new() { UnspecifiedSampleAspectRatio = default(SampleAspectRatio) };
            MediaBufferAllocator allocator = new(1024 * 1024);
            using MediaBuffer buffer = Copy(allocator, new byte[4]);
            VideoFormat format = new(1, 1, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb);
            using VideoFrame frame = new(format, buffer, format.CreateTightLayout());
            using MemoryStream encoded = new();
            await Assert.ThrowsAsync<ArgumentException>(() => MediaEngine.ExportImageAsync(frame, encoded, new PngImageEncoder(),
                allocator, VideoDisplayMatrix.Identity, options: options));
            Assert.Equal(0, encoded.Length);
        }

        private static MediaBuffer Copy(MediaBufferAllocator allocator, byte[] bytes)
        {
            using WritableMediaBuffer buffer = allocator.Allocate(bytes.Length);
            bytes.CopyTo(buffer.Span);
            return buffer.Publish();
        }
    }
}
