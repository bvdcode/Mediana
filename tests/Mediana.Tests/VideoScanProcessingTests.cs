// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Codecs.Png;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoScanProcessingTests
    {
        [Theory]
        [InlineData(VideoScanMode.TopFieldFirst)]
        [InlineData(VideoScanMode.BottomFieldFirst)]
        [InlineData(VideoScanMode.TopField)]
        [InlineData(VideoScanMode.BottomField)]
        public void ColorConversionPreservesFieldGeometry(VideoScanMode mode)
        {
            MediaBufferAllocator allocator = new(4096);
            VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709,
                ColorRange.Limited, ChromaLocation.Unspecified)
            {
                FieldChromaLocations = new(ChromaLocation.Left, ChromaLocation.Center)
            };
            VideoFormat format = new(4, 6, VideoPixelFormat.Yuv420P8, color, SampleAspectRatio.Square, null, mode);
            PlaneLayout[] planes = format.CreateTightLayout();
            using (WritableMediaBuffer writable = allocator.Allocate((int)planes[^1].EndOffset))
            {
                writable.Span.Fill(128);
                using MediaBuffer buffer = writable.Publish();
                using VideoFrame input = new(format, buffer, planes);
                using VideoFrame output = SrgbConverter.Apply(input, allocator);
                Assert.Equal(mode, output.Format.ScanMode);
                Assert.Equal(6, output.Format.Height);
                Assert.Equal(VideoColorInfo.Srgb, output.Format.Color);
                using VideoFrame horizontal = RgbaResize.Apply(output, 2, 6, allocator);
                Assert.Equal(mode, horizontal.Format.ScanMode);
                Assert.Throws<NotSupportedException>(() => RgbaResize.Apply(output, 2, 3, allocator));
                Assert.Throws<NotSupportedException>(() => SrgbPreview.Fit(input, VideoDisplayMatrix.Identity, 2, 3, allocator));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task AspectInterpretationPreservesScanModeBeforePreviewResizing()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat format = new(4, 8, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, null, null, VideoScanMode.TopFieldFirst);
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate((int)planes[0].EndOffset);
            using MediaBuffer buffer = writable.Publish();
            using VideoFrame frame = new(format, buffer, planes);
            VisualExportOptions options = new() { MaximumWidth = 2, MaximumHeight = 4, UnspecifiedSampleAspectRatio = SampleAspectRatio.Square };
            using MemoryStream destination = new();
            await Assert.ThrowsAsync<NotSupportedException>(() => MediaEngine.ExportImageAsync(frame, destination,
                new PngImageEncoder(), allocator, VideoDisplayMatrix.Identity, options: options));
        }

        [Fact]
        public void AColorInterpretationCannotEraseTheScanModeBeforePreparation()
        {
            VideoFormat format = new(4, 8, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.TopFieldFirst);
            MediaBufferAllocator allocator = new(4096);
            VideoPreparationOptions options = new()
            {
                SourceColorInterpretation = new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709,
                    ColorRange.Limited, ChromaLocation.Left)
            };
            Assert.Throws<NotSupportedException>(() => new VideoPreparation(format, VideoDisplayMatrix.Identity, allocator, options));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ChromaPolicyPreservesKnownIndependentFields()
        {
            VideoColorInfo color = new()
            {
                FieldChromaLocations = new(ChromaLocation.TopLeft, ChromaLocation.BottomLeft)
            };
            VideoFormat format = new(4, 8, VideoPixelFormat.Yuv420P8, color, null, null, VideoScanMode.TopFieldFirst);
            List<string> warnings = [];
            Assert.Equal(color, VideoColorPolicy.Resolve(format, VideoColorPolicy.ResolveChromaLocation, warnings.Add));
            Assert.Empty(warnings);
            VideoColorInfo incomplete = color with
            {
                FieldChromaLocations = new(ChromaLocation.Unspecified, ChromaLocation.BottomLeft)
            };
            format = new(4, 8, VideoPixelFormat.Yuv420P8, incomplete, null, null, VideoScanMode.TopFieldFirst);
            VideoColorInfo resolved = VideoColorPolicy.Resolve(format, VideoColorPolicy.ResolveChromaLocation, warnings.Add);
            Assert.Equal(new VideoFieldChromaLocations(ChromaLocation.Left, ChromaLocation.BottomLeft), resolved.FieldChromaLocations);
            Assert.Equal(ChromaLocation.Unspecified, resolved.ChromaLocation);
            Assert.NotEmpty(warnings);
        }
    }
}
