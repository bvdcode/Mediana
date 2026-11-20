// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Codecs.Png;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoFieldPreparationTests
    {
        [Theory]
        [InlineData(VideoScanMode.TopFieldFirst, VideoFieldSelection.Leading, 10)]
        [InlineData(VideoScanMode.BottomFieldFirst, VideoFieldSelection.Leading, 220)]
        [InlineData(VideoScanMode.TopFieldFirst, VideoFieldSelection.Bottom, 220)]
        [InlineData(VideoScanMode.BottomFieldFirst, VideoFieldSelection.Top, 10)]
        [InlineData(VideoScanMode.TopField, VideoFieldSelection.Leading, 10)]
        [InlineData(VideoScanMode.BottomField, VideoFieldSelection.Leading, 220)]
        public void SelectsActualFieldOrderAndAllocatesOnlyProgressiveOutput(VideoScanMode mode, VideoFieldSelection selection, byte expected)
        {
            MediaBufferAllocator allocator = new(4096);
            List<string> warnings = [];
            VideoFormat format = Format(mode);
            using VideoFrame input = Create(format, allocator);
            long before = allocator.AllocatedBytes;
            VideoFieldPreparation preparation = new(format, selection, allocator, warnings.Add);
            Assert.True(preparation.RequiresInterpolation);
            Assert.Equal(format.FullFrameHeight, preparation.OutputFormat.Height);
            for (int repeat = 0; repeat < 2; repeat++)
            {
                using VideoFrame output = preparation.Apply(input);
                Assert.Same(preparation.OutputFormat, output.Format);
                Assert.Equal(VideoScanMode.Progressive, output.Format.ScanMode);
                Assert.Equal(before + output.Planes[^1].EndOffset, allocator.AllocatedBytes);
                Assert.Equal(input.Timestamp, output.Timestamp);
                Assert.Equal(input.Duration, output.Duration);
                for (int plane = 0; plane < output.Planes.Count; plane++)
                {
                    for (int row = 0; row < output.Planes[plane].Height; row++)
                    {
                        Assert.True(output.GetRow(plane, row).IndexOfAnyExcept(expected) < 0);
                    }
                }
            }
            Assert.Single(warnings);
            Assert.Equal(before, allocator.AllocatedBytes);
        }

        [Fact]
        public void ProgressiveInputRetainsPixelsAndProducesNoWarning()
        {
            MediaBufferAllocator allocator = new(4096);
            using VideoFrame input = Create(Format(VideoScanMode.Progressive), allocator);
            List<string> warnings = [];
            VideoFieldPreparation preparation = new(input.Format, VideoFieldSelection.Leading, allocator, warnings.Add);
            long before = allocator.AllocatedBytes;
            using VideoFrame output = preparation.Apply(input);
            Assert.False(preparation.RequiresInterpolation);
            Assert.Equal(before, allocator.AllocatedBytes);
            Assert.Equal(input.Data, output.Data);
            Assert.Same(input.Format, output.Format);
            Assert.Empty(warnings);
        }

        [Fact]
        public void AcceptsEquivalentPixelDescriptionsAndRejectsChangedGeometryOrColor()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat format = Format(VideoScanMode.TopField);
            using VideoFrame input = Create(format, allocator);
            VideoFieldPreparation preparation = new(format, VideoFieldSelection.Leading, allocator);
            VideoFormat equivalent = new(format.Width, format.Height,
                new PlanarVideoPixelFormat("another-name", PlanarColorModel.Yuv, 8, 2, 2), format.Color, format.SampleAspectRatio,
                null, format.ScanMode, format.FullFrameHeight);
            using VideoFrame same = input.WithFormat(equivalent);
            using VideoFrame output = preparation.Apply(same);
            using VideoFrame changed = input.WithFormat(new(format.Width, format.Height, format.PixelFormat, format.Color,
                format.SampleAspectRatio, null, format.ScanMode, 6));
            Assert.Throws<ArgumentException>(() => preparation.Apply(changed));
            using VideoFrame recolored = input.WithFormat(new(format.Width, format.Height, format.PixelFormat,
                format.Color with { Range = ColorRange.Limited }, format.SampleAspectRatio, null, format.ScanMode, format.FullFrameHeight));
            Assert.Throws<ArgumentException>(() => preparation.Apply(recolored));
            Assert.Throws<OperationCanceledException>(() => preparation.Apply(input, new(true)));
        }

        [Fact]
        public void DoesNotGuessUnknownHeightOrSubstituteAnAbsentField()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat unknown = new(4, 3, VideoPixelFormat.Yuv420P8, null, null, null, VideoScanMode.TopField);
            Assert.Throws<NotSupportedException>(() => new VideoFieldPreparation(unknown, VideoFieldSelection.Leading, allocator));
            Assert.Throws<ArgumentException>(() => new VideoFieldPreparation(Format(VideoScanMode.TopField), VideoFieldSelection.Bottom, allocator));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VideoFieldPreparation(Format(VideoScanMode.Progressive), (VideoFieldSelection)99, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void EncodingPreparationRequiresOptInAndInterpolatesBeforeVerticalScaling()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat format = Format(VideoScanMode.BottomFieldFirst);
            using VideoFrame input = Create(format, allocator);
            Assert.Throws<NotSupportedException>(() => new VideoPreparation(format, VideoDisplayMatrix.Identity, allocator));
            using VideoPreparation preparation = new(format, VideoDisplayMatrix.Identity, allocator, new()
            {
                InterlacedField = VideoFieldSelection.Leading,
                MaximumWidth = 2,
                MaximumHeight = 2
            });
            using VideoFrame output = preparation.Apply(input);
            Assert.Equal(VideoScanMode.Progressive, output.Format.ScanMode);
            Assert.Equal(2, output.Format.Width);
            Assert.Equal(2, output.Format.Height);
            Assert.True(output.GetRow(0, 0).IndexOfAnyExcept((byte)220) < 0);
            Assert.Equal(input.Timestamp, output.Timestamp);
            Assert.Equal(input.Duration, output.Duration);
        }

        [Fact]
        public void MixedFrameAndFieldSequenceReportsTheSameInterpretationOnlyOnce()
        {
            MediaBufferAllocator allocator = new(4096);
            using VideoFrame fields = Create(Format(VideoScanMode.TopFieldFirst), allocator);
            using VideoFrame progressive = Create(Format(VideoScanMode.Progressive), allocator);
            List<string> warnings = [];
            using VideoPreparationSequence preparation = new(fields.Format, VideoDisplayMatrix.Identity, allocator,
                new() { InterlacedField = VideoFieldSelection.Leading, WarningHandler = warnings.Add });
            foreach (VideoFrame input in new[] { fields, progressive, fields })
            {
                using VideoFrame output = preparation.Apply(input);
                Assert.Equal(VideoScanMode.Progressive, output.Format.ScanMode);
            }
            Assert.Single(warnings);
        }

        [Fact]
        public async Task VisualPreparationAppliesTheSameFieldPolicyBeforeColorAndResize()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            VideoFormat format = Format(VideoScanMode.TopFieldFirst);
            using VideoFrame input = Create(format, allocator);
            List<string> warnings = [];
            using MemoryStream destination = new();
            await MediaEngine.ExportImageAsync(input, destination, new PngImageEncoder(), allocator, VideoDisplayMatrix.Identity,
                options: new() { MaximumWidth = 2, MaximumHeight = 2, InterlacedField = VideoFieldSelection.Leading, WarningHandler = warnings.Add });
            using VideoFrame output = await new PngImageDecoder().DecodeAsync(destination.ToArray(), new(), allocator);
            Assert.Equal(VideoScanMode.Progressive, output.Format.ScanMode);
            Assert.Equal(VideoPixelFormat.Rgba32, output.Format.PixelFormat);
            Assert.Equal(2, output.Format.Width);
            Assert.Equal(2, output.Format.Height);
            Assert.True(output.GetRow(0, 0).SequenceEqual(output.GetRow(0, 1)));
            Assert.Single(warnings);
        }

        private static VideoFormat Format(VideoScanMode mode)
        {
            int height = mode switch
            {
                VideoScanMode.TopField or VideoScanMode.BottomField => 3,
                VideoScanMode.Progressive or VideoScanMode.TopFieldFirst or VideoScanMode.BottomFieldFirst => 6,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            int fullHeight = mode switch
            {
                VideoScanMode.TopField => 5,
                VideoScanMode.BottomField => 7,
                VideoScanMode.Progressive or VideoScanMode.TopFieldFirst or VideoScanMode.BottomFieldFirst => 6,
                _ => throw new ArgumentOutOfRangeException(nameof(mode))
            };
            return new(6, height, VideoPixelFormat.Yuv420P8,
                new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Full, ChromaLocation.Left),
                SampleAspectRatio.Square, null, mode, fullHeight);
        }

        private static VideoFrame Create(VideoFormat format, MediaBufferAllocator allocator)
        {
            PlaneLayout[] planes = format.CreateTightLayout();
            using WritableMediaBuffer writable = allocator.Allocate(checked((int)planes[^1].EndOffset));
            for (int plane = 0; plane < planes.Length; plane++)
            {
                for (int row = 0; row < planes[plane].Height; row++)
                {
                    byte value = format.ScanMode switch
                    {
                        VideoScanMode.Progressive or VideoScanMode.TopField => 10,
                        VideoScanMode.BottomField => 220,
                        VideoScanMode.TopFieldFirst or VideoScanMode.BottomFieldFirst => (row & 1) == 0 ? (byte)10 : (byte)220,
                        _ => throw new ArgumentOutOfRangeException(nameof(format))
                    };
                    writable.Span.Slice(planes[plane].Offset + row * planes[plane].Stride, planes[plane].RowBytes).Fill(value);
                }
            }
            using MediaBuffer buffer = writable.Publish();
            return new(format, buffer, planes, new(1, new(1, 25)), new(1, new(1, 25)));
        }
    }
}
