// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class Mp4AspectRatioTests
    {
        [Theory]
        [InlineData(false, 8u, 6u, 4, 3)]
        [InlineData(true, 10u, 11u, 10, 11)]
        [InlineData(false, uint.MaxValue, uint.MaxValue, 1, 1)]
        public async Task ExplicitPixelSpacingIsReducedAndPreservedAcrossMovieTimeMapping(bool quickTime,
            uint horizontal, uint vertical, int expectedWidth, int expectedHeight)
        {
            byte[] file = await InputAsync(quickTime, Spacing(horizontal, vertical));
            file = Mp4EditTestData.Replace(file, false, (1, -1, 65536), (1000, 0, 65536));
            MediaBufferAllocator allocator = new(128 * 1024);
            await using TrackingRandomAccessSource source = new(file, 7);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new Mp4ReadOptions { TimestampMode = Mp4TimestampMode.LinearMovie });
            EncodedStreamInfo original = Assert.Single(reader.Tracks).Stream;
            EncodedStreamInfo mapped = Assert.Single(reader.Streams);
            VideoAspectRatioDescription description = Assert.IsType<VideoAspectRatioDescription>(mapped.AspectRatioDescription);
            Assert.Equal(new SampleAspectRatio(expectedWidth, expectedHeight), description.Value);
            Assert.False(description.IsDefault);
            Assert.NotEqual(original.TimeBase, mapped.TimeBase);
            Assert.Same(original.AspectRatioDescription, description);
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task OnlyQuickTimeDefinesImplicitSquarePixels(bool quickTime)
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            await using TrackingRandomAccessSource source = new(await InputAsync(quickTime));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            VideoAspectRatioDescription? description = Assert.Single(reader.Streams).AspectRatioDescription;
            if (quickTime)
            {
                Assert.NotNull(description);
                Assert.True(description.IsDefault);
                Assert.Equal(SampleAspectRatio.Square, description.Value);
            }
            else
            {
                Assert.Null(description);
            }
        }

        [Theory]
        [InlineData(false, true, 2, 1)]
        [InlineData(true, true, 2, 1)]
        [InlineData(false, false, 4, 3)]
        [InlineData(true, false, 4, 3)]
        public async Task DecoderUsesDeclaredContainerSpacingAndPreservesCodecSpacingWhenAbsent(bool quickTime,
            bool explicitSpacing, int width, int height)
        {
            byte[] file = explicitSpacing ? await InputAsync(quickTime, Spacing(2, 1)) : await InputAsync(quickTime);
            await using TrackingRandomAccessSource source = new(file);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using VideoSession session = await engine.OpenVideoAsync(source);
            using VideoFrame frame = Assert.IsType<VideoFrame>(await session.ReadFrameAsync());
            Assert.Equal(new SampleAspectRatio(width, height), frame.Format.SampleAspectRatio);
            byte[] pixels = await Mp4TestData.ReadAsync("avc-weighted.sequence.yuv");
            H264TestInput.VerifyFrame(frame, pixels.AsSpan(0, 94 * 62 * 3 / 2).ToArray());
            frame.Dispose();
            await session.DisposeAsync();
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task InvalidAndDuplicatedSpacingFailsWithoutRetainedStorage()
        {
            byte[][] invalid = [[], new byte[7], new byte[9], Spacing(0, 1), Spacing(1, 0)];
            foreach (byte[] payload in invalid)
            {
                await RejectAsync<InvalidDataException>(await InputAsync(false, payload));
            }
            byte[] duplicate = Mp4TestData.AppendToVisualDescription(await InputAsync(false, Spacing(1, 1)),
                Mp4MetadataTestData.Box("pasp", Spacing(2, 1)));
            await RejectAsync<InvalidDataException>(duplicate);
            await RejectAsync<NotSupportedException>(await InputAsync(false, Spacing(uint.MaxValue, 1)));
        }

        private static async Task RejectAsync<T>(byte[] file) where T : Exception
        {
            MediaBufferAllocator allocator = new(128 * 1024);
            await using TrackingRandomAccessSource source = new(file);
            await Assert.ThrowsAsync<T>(async () => await Mp4Reader.OpenAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        private static async Task<byte[]> InputAsync(bool quickTime, byte[]? spacing = null)
        {
            byte[] file = await Mp4TestData.ReadAsync("avc-weighted.mp4");
            Mp4TestData.SetBrand(file, quickTime ? "qt  "u8 : "isom"u8);
            int pasp = Mp4TestData.PayloadOffset(file, "pasp");
            "free"u8.CopyTo(file.AsSpan(pasp - 4, 4));
            if (spacing is not null)
            {
                file = Mp4TestData.AppendToVisualDescription(file, Mp4MetadataTestData.Box("pasp", spacing));
            }
            return file;
        }

        private static byte[] Spacing(uint horizontal, uint vertical)
        {
            byte[] result = new byte[8];
            BinaryPrimitives.WriteUInt32BigEndian(result, horizontal);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4), vertical);
            return result;
        }
    }
}
