// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VideoColorInterpretationTests
    {
        private static readonly VideoColorInfo Color = new(ColorPrimaries.Bt709, TransferFunction.Bt709,
            MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left);

        [Fact]
        public void ExplicitInterpretationRetainsPixelsAndPreservesSourceMetadata()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat source = new(16, 12, VideoPixelFormat.Yuv420P8);
            using (VideoFrame input = PlanarSrgbTests.Create(source, allocator, (plane, x, y) => plane * 32 + x + y))
            using (VideoPreparation preparation = new(source, VideoDisplayMatrix.Identity, allocator,
                new() { SourceColorInterpretation = Color }))
            {
                long held = allocator.AllocatedBytes;
                using VideoFrame output = preparation.Apply(input);
                Assert.Equal(held, allocator.AllocatedBytes);
                Assert.True(input.Data.Equals(output.Data));
                Assert.Equal(Color, output.Format.GetColorInterpretation());
                Assert.Equal(new VideoColorInfo(), input.Format.Color);
                Assert.Null(output.Format.ColorDescription);
                Assert.Equal(input.Timestamp, output.Timestamp);
                Assert.Equal(input.Duration, output.Duration);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void UnknownChromaRemainsStrictUnlessCallerProvidesInterpretation()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat source = new(16, 12, VideoPixelFormat.Yuv420P8);
            Assert.Throws<NotSupportedException>(() => new VideoPreparation(source, VideoDisplayMatrix.Identity,
                allocator, new() { MaximumWidth = 8 }));
            Assert.Equal(0, allocator.AllocatedBytes);
            using (VideoFrame input = PlanarSrgbTests.Create(source, allocator, (_, _, _) => 128))
            using (VideoPreparation preparation = new(source, VideoDisplayMatrix.Identity, allocator,
                new() { MaximumWidth = 8, SourceColorInterpretation = Color }))
            using (VideoFrame output = preparation.Apply(input))
            {
                Assert.Equal(8, output.Format.Width);
                Assert.Equal(6, output.Format.Height);
                Assert.Equal(Color, output.Format.GetColorInterpretation());
                Assert.All(output.Data.ToArray(), sample => Assert.Equal(128, sample));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ExplicitSequenceInterpretationReplacesSourceDescriptionPrecedence()
        {
            MediaBufferAllocator allocator = new(4096);
            CicpColorDescription description = new(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
            VideoFormat source = new(16, 12, VideoPixelFormat.Yuv420P8, colorDescription: description);
            Assert.Throws<NotSupportedException>(() => source.GetColorInterpretation());
            using (VideoPreparationSequence preparation = new(source, VideoDisplayMatrix.Identity, allocator,
                new() { SourceColorInterpretation = Color }))
            {
                foreach (VideoFormat format in new[] { source, new VideoFormat(16, 12, VideoPixelFormat.Yuv420P8, VideoColorInfo.Srgb) })
                {
                    using VideoFrame input = PlanarSrgbTests.Create(format, allocator, (_, _, _) => 128);
                    long held = allocator.AllocatedBytes;
                    using VideoFrame output = preparation.Apply(input);
                    Assert.Same(preparation.OutputFormat, output.Format);
                    Assert.Equal(Color, output.Format.GetColorInterpretation());
                    Assert.Equal(held, allocator.AllocatedBytes);
                    Assert.True(input.Data.Equals(output.Data));
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ReinterpretationCannotHideGeometryChangesOrCancellation()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat source = new(16, 12, VideoPixelFormat.Yuv420P8);
            using (VideoFrame different = PlanarSrgbTests.Create(new(8, 12, VideoPixelFormat.Yuv420P8), allocator, (_, _, _) => 0))
            using (VideoPreparation preparation = new(source, VideoDisplayMatrix.Identity, allocator,
                new() { SourceColorInterpretation = Color }))
            {
                long held = allocator.AllocatedBytes;
                Assert.Throws<NotSupportedException>(() => preparation.Apply(different));
                Assert.Throws<OperationCanceledException>(() => preparation.Apply(different, new(true)));
                Assert.Equal(held, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void SelectedPolicyResolvesOncePerFormatAndPreservesSamplesAndSourceLabels()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat source = new(16, 12, VideoPixelFormat.Yuv420P8);
            List<string> warnings = [];
            using (VideoPreparationSequence preparation = new(source, VideoDisplayMatrix.Identity, allocator,
                new() { ColorResolver = SdrPreviewColorPolicy.Resolve, WarningHandler = warnings.Add }))
            {
                for (int index = 0; index < 3; index++)
                {
                    using VideoFrame input = PlanarSrgbTests.Create(source, allocator, (plane, x, y) => plane * 32 + x + y);
                    long held = allocator.AllocatedBytes;
                    using VideoFrame output = preparation.Apply(input);
                    Assert.Equal(held, allocator.AllocatedBytes);
                    Assert.True(input.Data.Equals(output.Data));
                    Assert.Equal(ChromaLocation.Left, output.Format.Color.ChromaLocation);
                    Assert.Equal(new VideoColorInfo(), input.Format.Color);
                }
            }
            Assert.Contains("chroma location", Assert.Single(warnings), StringComparison.Ordinal);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ExplicitInterpretationTakesPriorityOverPolicy()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat source = new(16, 12, VideoPixelFormat.Yuv420P8);
            using VideoPreparation preparation = new(source, VideoDisplayMatrix.Identity, allocator, new()
            {
                SourceColorInterpretation = Color,
                ColorResolver = _ => throw new InvalidOperationException("Explicit interpretation must take priority.")
            });
            Assert.Equal(Color, preparation.OutputFormat.Color);
            Assert.Equal(new VideoColorInfo(), source.Color);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void ThrowingPrecisionWarningDoesNotRetainResizingWorkspace()
        {
            MediaBufferAllocator allocator = new(4096);
            VideoFormat source = new(16, 12, VideoPixelFormat.Yuv420P10, Color);
            Assert.Throws<OperationCanceledException>(() => new VideoPreparation(source, VideoDisplayMatrix.Identity, allocator, new()
            {
                MaximumWidth = 8,
                OutputPixelFormat = VideoPixelFormat.Yuv420P8,
                WarningHandler = _ => throw new OperationCanceledException()
            }));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
