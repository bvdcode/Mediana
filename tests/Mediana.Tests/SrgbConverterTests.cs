// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class SrgbConverterTests
    {
        private static readonly VideoColorInfo Srgb = new(ColorPrimaries.Bt709, TransferFunction.Srgb,
            MatrixCoefficients.Rgb, ColorRange.Full, ChromaLocation.Unspecified);

        [Fact]
        public void SrgbRoundtripPreservesEveryByteIncludingStraightAlphaAndOutputOwnsItsStorage()
        {
            byte[] pixels = new byte[256 * 4];
            for (int index = 0; index < 256; index++)
            {
                pixels[index * 4] = (byte)index;
                pixels[index * 4 + 1] = (byte)index;
                pixels[index * 4 + 2] = (byte)index;
                pixels[index * 4 + 3] = (byte)(255 - index);
            }
            MediaBufferAllocator allocator = new(4096);
            using VideoFrame input = Frame(allocator, new VideoFormat(256, 1, VideoPixelFormat.Rgba32, Srgb), pixels);
            using VideoFrame output = SrgbConverter.Apply(input, allocator);
            input.Dispose();
            Assert.Equal(pixels, output.Data.ToArray());
            Assert.Equal(Srgb, output.Format.Color);
            Assert.Equal(pixels.Length, allocator.AllocatedBytes);
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(TransferFunction.Bt709, 121)]
        [InlineData(TransferFunction.Srgb, 128)]
        [InlineData(TransferFunction.Linear, 188)]
        [InlineData(TransferFunction.Gamma22, 129)]
        [InlineData(TransferFunction.Gamma28, 106)]
        [InlineData(TransferFunction.Smpte240M, 121)]
        public void AppliesReferenceDisplayTransferBeforeEncodingSrgb(TransferFunction transfer, byte expected)
        {
            MediaBufferAllocator allocator = new(16);
            using VideoFrame input = Frame(allocator, new VideoFormat(1, 1, VideoPixelFormat.Rgba32, Srgb with { Transfer = transfer }),
                [128, 128, 128, 17]);
            using VideoFrame output = SrgbConverter.Apply(input, allocator);
            Assert.Equal(new byte[] { expected, expected, expected, 17 }, output.Data.ToArray());
        }

        [Fact]
        public void OddYuvDimensionsAndPaddedPlanesPreserveTimeAspectAndExactBlackWhite()
        {
            byte[] pixels = new byte[40];
            for (int row = 0; row < 3; row++)
            {
                pixels[1 + row * 5] = 16;
                pixels[2 + row * 5] = 235;
                pixels[3 + row * 5] = 126;
            }
            pixels.AsSpan(20, 20).Fill(128);
            VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left);
            MediaBufferAllocator allocator = new(1024);
            MediaTimestamp timestamp = new(7, new TimeBase(1, 30));
            MediaDuration duration = new(1, new TimeBase(1, 30));
            using (VideoFrame input = Frame(allocator, new VideoFormat(3, 3, VideoPixelFormat.Yuv420P8, color, new SampleAspectRatio(4, 3)),
                pixels, [new PlaneLayout(1, 5, 3, 3), new PlaneLayout(20, 4, 2, 2), new PlaneLayout(30, 4, 2, 2)], timestamp, duration))
            using (VideoFrame output = SrgbConverter.Apply(input, allocator))
            {
                byte[] expectedRow = [0, 0, 0, 255, 255, 255, 255, 255, 121, 121, 121, 255];
                for (int row = 0; row < 3; row++)
                {
                    Assert.Equal(expectedRow, output.GetRow(0, row).ToArray());
                }
                Assert.Equal(timestamp, output.Timestamp);
                Assert.Equal(duration, output.Duration);
                Assert.Equal(new SampleAspectRatio(4, 3), output.Format.SampleAspectRatio);
                Assert.Equal(Srgb, output.Format.Color);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void UnknownColorRequiresAnExplicitInterpretationAndDoesNotChangeInputMetadata()
        {
            MediaBufferAllocator allocator = new(64);
            using VideoFrame input = Frame(allocator, new VideoFormat(1, 1, VideoPixelFormat.Rgba32), [1, 2, 3, 4]);
            Assert.Throws<NotSupportedException>(() => SrgbConverter.Apply(input, allocator));
            Assert.Equal(4, allocator.AllocatedBytes);
            using VideoFrame output = SrgbConverter.Apply(input, Srgb, allocator);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, output.Data.ToArray());
            Assert.Equal(new VideoColorInfo(), input.Format.Color);
            Assert.Equal(Srgb, output.Format.Color);
        }

        [Theory]
        [InlineData(TransferFunction.Pq)]
        [InlineData(TransferFunction.Hlg)]
        public void HdrRequiresToneMappingAndFailureOrCancellationDoesNotLeak(TransferFunction transfer)
        {
            MediaBufferAllocator allocator = new(4);
            using VideoFrame input = Frame(allocator, new VideoFormat(1, 1, VideoPixelFormat.Rgba32, Srgb with { Transfer = transfer }), [1, 2, 3, 4]);
            Assert.Throws<NotSupportedException>(() => SrgbConverter.Apply(input, allocator));
            Assert.Throws<OperationCanceledException>(() => SrgbConverter.Apply(input, Srgb, allocator, new CancellationToken(true)));
            Assert.Throws<MediaMemoryLimitException>(() => SrgbConverter.Apply(input, Srgb, allocator));
            Assert.Equal(4, allocator.AllocatedBytes);
        }

        [Fact]
        public void MissingYuvInterpretationAndInvalidEnumValuesLeaveInputAndAllocationUnchanged()
        {
            MediaBufferAllocator allocator = new(64);
            VideoColorInfo color = new(ColorPrimaries.Bt709, TransferFunction.Srgb, MatrixCoefficients.Bt709, ColorRange.Full, ChromaLocation.Left);
            using VideoFrame input = Frame(allocator, new VideoFormat(2, 2, VideoPixelFormat.Yuv420P8, color), [16, 16, 16, 16, 128, 128]);
            Assert.Throws<NotSupportedException>(() => SrgbConverter.Apply(input, color with { Matrix = MatrixCoefficients.Unspecified }, allocator));
            Assert.Throws<NotSupportedException>(() => SrgbConverter.Apply(input, color with { Range = ColorRange.Unspecified }, allocator));
            Assert.Throws<NotSupportedException>(() => SrgbConverter.Apply(input, color with { ChromaLocation = ChromaLocation.Unspecified }, allocator));
            Assert.Throws<ArgumentOutOfRangeException>(() => SrgbConverter.Apply(input, color with { Primaries = (ColorPrimaries)int.MaxValue }, allocator));
            Assert.Equal(color, input.Format.Color);
            Assert.Equal(6, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RealDecodedFrameMatchesAnIndependentSrgbReference()
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Mp4TestData.PathFor("avc-weighted.mp4")));
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame decoded = Assert.IsType<VideoFrame>(await session.ReadFrameAsync());
            MediaBufferAllocator allocator = new(128 * 1024);
            using VideoFrame output = SrgbConverter.Apply(decoded, allocator);
            byte[] expected = await Mp4TestData.ReadAsync("avc-weighted.frame0.srgb.rgba");
            Assert.Equal(expected.Length, output.Data.Length);
            int maximumDifference = 0;
            for (int index = 0; index < expected.Length; index++)
            {
                maximumDifference = Math.Max(maximumDifference, Math.Abs(expected[index] - output.Data.Span[index]));
            }
            Assert.InRange(maximumDifference, 0, 1);
            Assert.Equal(Srgb, output.Format.Color);
            Assert.Equal(decoded.Timestamp, output.Timestamp);
            output.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static VideoFrame Frame(MediaBufferAllocator allocator, VideoFormat format, byte[] pixels,
            PlaneLayout[]? planes = null, MediaTimestamp? timestamp = null, MediaDuration? duration = null)
        {
            using WritableMediaBuffer writable = allocator.Allocate(pixels.Length);
            pixels.CopyTo(writable.Memory);
            using MediaBuffer buffer = writable.Publish();
            return new VideoFrame(format, buffer, planes ?? format.CreateTightLayout(), timestamp, duration);
        }
    }
}
