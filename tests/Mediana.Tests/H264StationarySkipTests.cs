// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class H264StationarySkipTests
    {
        [Fact]
        public void StridedReferenceRunStopsAtChromaMismatchAndCroppedEdges()
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(50, 34);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
            using H264Picture reference = new(configuration.Sequences[0], allocator, H264SliceKind.P);
            using VideoFrame frame = H264EncoderTests.Frame(format, allocator, 0);
            CopyFrameToReference(frame, reference);
            H264SourcePicture source = new(frame);
            H264SkipResidualBounds bounds = H264ForwardTransform.GetSkipBounds(30);

            Assert.Equal(3, source.MatchingStationaryRun(reference, 0, 0, bounds));
            Assert.Equal(0, source.MatchingStationaryRun(reference, 48, 0, bounds));
            Assert.Equal(0, source.MatchingStationaryRun(reference, 0, 32, bounds));

            reference.Plane(2)[7 * reference.Stride(2) + 16] += 20;
            Assert.Equal(2, source.MatchingStationaryRun(reference, 0, 0, bounds));
            Assert.Equal(0, source.MatchingStationaryRun(reference, 32, 0, bounds));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void TexturedSkipChecksTheLastSampleOfEveryPlane(int changedPlane)
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(32, 32);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
            using H264Picture reference = new(configuration.Sequences[0], allocator, H264SliceKind.P);
            using VideoFrame frame = H264EncoderTests.Frame(format, allocator, 0);
            CopyFrameToReference(frame, reference);
            Span<byte> storage = stackalloc byte[H264SourceMacroblock.ByteCount];
            H264SourceMacroblock source = new(frame, 16, 16, storage);
            Assert.True(source.Luma.ContainsAnyExcept(source.Luma[0]));
            H264SkipResidualBounds bounds = H264ForwardTransform.GetSkipBounds(30);
            Assert.True(bounds.AllowsStationarySkip(source, reference, 16, 16));
            ref byte sample = ref reference.Plane(changedPlane)[^1];
            byte original = sample;
            sample = (byte)(original + 1);
            Assert.True(bounds.AllowsStationarySkip(source, reference, 16, 16));
            Assert.False(H264ForwardTransform.GetSkipBounds(0).AllowsStationarySkip(source, reference, 16, 16));
            byte limit = changedPlane == 0 ? bounds.Luma : bounds.Chroma;
            sample = (byte)(original + limit);
            Assert.True(bounds.AllowsStationarySkip(source, reference, 16, 16));
            sample++;
            Assert.False(bounds.AllowsStationarySkip(source, reference, 16, 16));
        }

        [Theory]
        [InlineData(0, 16)]
        [InlineData(1, 8)]
        [InlineData(2, 8)]
        public void StationarySkipBoundsTheMeanSquaredErrorOfEachPlane(int changedPlane, int width)
        {
            MediaBufferAllocator allocator = new(65536);
            VideoFormat format = H264EncoderTests.Format(16, 16);
            using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator);
            H264Configuration configuration = H264Configuration.Parse(encoder.Stream.Configuration.Span, new());
            using H264Picture reference = new(configuration.Sequences[0], allocator, H264SliceKind.P);
            using VideoFrame frame = H264EncoderTests.Frame(format, allocator, 0);
            CopyFrameToReference(frame, reference);
            Span<byte> storage = stackalloc byte[H264SourceMacroblock.ByteCount];
            H264SourceMacroblock source = new(frame, 0, 0, storage);
            H264SkipResidualBounds bounds = new(byte.MaxValue, byte.MaxValue);
            Span<byte> previous = reference.Plane(changedPlane);
            previous[^1] += (byte)width;
            Assert.True(bounds.AllowsStationarySkip(source, reference, 0, 0));
            previous[0]++;
            Assert.False(bounds.AllowsStationarySkip(source, reference, 0, 0));
            CopyFrameToReference(frame, reference);
            for (int pixel = 0; pixel < previous.Length; pixel++)
            {
                previous[pixel] += (byte)width;
            }
            Assert.False(bounds.AllowsStationarySkip(source, reference, 0, 0));
        }

        private static void CopyFrameToReference(VideoFrame frame, H264Picture reference)
        {
            for (int plane = 0; plane < 3; plane++)
            {
                for (int y = 0; y < frame.Planes[plane].Height; y++)
                {
                    frame.GetRow(plane, y).CopyTo(reference.Plane(plane)[(y * reference.Stride(plane))..]);
                }
            }
        }
    }
}
