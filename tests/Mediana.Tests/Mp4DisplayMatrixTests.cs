// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class Mp4DisplayMatrixTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task VersionZeroAndOneHeadersPreserveMovieAndTrackMatricesAndTheirComposition(bool wideHeaders)
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            if (wideHeaders)
            {
                data = Mp4TestData.Use64BitTimeHeaders(data);
            }
            SetMatrix(data, "tkhd", 40, [0, 65536, 0, -65536, 0, 0, 48 * 65536, 0, 1 << 30]);
            SetMatrix(data, "mvhd", 36, [-65536, 0, 0, 0, 65536, 0, 5 * 65536, 7 * 65536, 1 << 30]);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 13);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Equal(new VideoDisplayMatrix(-1, 0, 0, 0, 1, 0, 5, 7, 1), reader.DisplayMatrix);
            Mp4TrackInfo track = Assert.Single(reader.Tracks);
            Assert.Equal(new VideoDisplayMatrix(0, 1, 0, -1, 0, 0, 48, 0, 1), track.DisplayMatrix);
            Assert.Equal(new VideoDisplayMatrix(0, 1, 0, 1, 0, 0, -43, 7, 1), track.Stream.DisplayMatrix);
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FixedPointFractionsAndProjectiveTermsArePreservedWithoutInferringAnAngle()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            SetMatrix(data, "tkhd", 40, [32768, 1, 3, -2, int.MinValue, 4, -16384, 98304, 1 << 30]);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            VideoDisplayMatrix expected = new(0.5, 1.0 / 65536, 3.0 / 1073741824, -2.0 / 65536, -32768,
                4.0 / 1073741824, -0.25, 1.5, 1);
            Assert.Equal(expected, reader.Tracks[0].DisplayMatrix);
            Assert.Equal(expected, reader.Streams[0].DisplayMatrix);
            Assert.Equal(VideoDisplayMatrix.Identity, reader.DisplayMatrix);
        }

        [Fact]
        public async Task VideoSessionExposesContainerGeometryWithoutChangingDecodedStorage()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-tail.mov");
            SetMatrix(data, "tkhd", 40, [0, 65536, 0, -65536, 0, 0, 0, 0, 1 << 30]);
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using TrackingRandomAccessSource source = new(data);
            await using VideoSession session = await engine.OpenVideoAsync(source, 0);
            using VideoFrame frame = Assert.IsType<VideoFrame>(await session.ReadFrameAsync());
            Assert.Equal(80, frame.Format.Width);
            Assert.Equal(48, frame.Format.Height);
            frame.Dispose();
            await session.DisposeAsync();
            Assert.Equal(new VideoDisplayMatrix(0, 1, 0, -1, 0, 0, 0, 0, 1), session.Stream.DisplayMatrix);
            Assert.Equal(0, session.AllocatedBufferBytes);
        }

        [Fact]
        public async Task AudioDoesNotAcquireVideoDisplayMetadata()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            Assert.Equal(VideoDisplayMatrix.Identity, reader.Streams[0].DisplayMatrix);
            Assert.Null(reader.Streams[1].DisplayMatrix);
        }

        private static void SetMatrix(byte[] data, string box, int versionZeroOffset, int[] coefficients)
        {
            int payload = Mp4TestData.PayloadOffset(data, box);
            int offset = payload + versionZeroOffset + (data[payload] == 0 ? 0 : 12);
            for (int index = 0; index < coefficients.Length; index++)
            {
                BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset + index * 4, 4), coefficients[index]);
            }
        }
    }
}
