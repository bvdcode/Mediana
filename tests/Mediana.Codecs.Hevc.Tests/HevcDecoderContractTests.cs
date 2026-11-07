// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Codecs.Hevc.Tests
{
    public class HevcDecoderContractTests
    {
        [Theory]
        [InlineData("main8")]
        [InlineData("main10")]
        public async Task BackpressureDoesNotConsumePacketsAndResetRestoresDecoderAfterCancellation(string name)
        {
            MediaBufferAllocator inputAllocator = new(1024 * 1024), allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", name + ".mp4")));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, inputAllocator);
            using IMediaCodec<MediaPacket, VideoFrame> decoder = new HevcDecoderFactory().Create(reader.Streams[0], allocator);
            int next = 0;
            while (true)
            {
                using MediaPacket packet = await reader.ReadPacketAtAsync(0, next++);
                if (decoder.TrySend(packet) == CodecInputStatus.OutputRequired) { break; }
            }
            using VideoFrame retained = decoder.Receive().Value;
            byte[] original = retained.Data.ToArray();
            decoder.Reset();
            Assert.Equal(original, retained.Data.ToArray());
            retained.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
            using MediaPacket first = await reader.ReadPacketAtAsync(0, 0);
            using CancellationTokenSource cancellation = new(); await cancellation.CancelAsync();
            Assert.ThrowsAny<OperationCanceledException>(() => decoder.TrySend(first, cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<InvalidOperationException>(() => decoder.Receive());
            decoder.Reset();
            Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
            decoder.CompleteInput();
            using VideoFrame restarted = decoder.Receive().Value;
            Assert.Equal(original, restarted.Data.ToArray());
            Assert.Equal(CodecOutputStatus.EndOfStream, decoder.Receive().Status);
            restarted.Dispose(); decoder.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PredictiveStartIsRejectedAndRandomAccessComesFromSliceHeaders()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using RandomAccessStreamByteSource source = new(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "main8.mp4")));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            HevcDecoderFactory factory = new();
            using IMediaCodec<MediaPacket, VideoFrame> decoder = factory.Create(reader.Streams[0], allocator);
            IRandomAccessInspector inspector = factory.CreateRandomAccessInspector(reader.Streams[0]);
            using MediaPacket first = await reader.ReadPacketAtAsync(0, 0);
            using MediaPacket predictive = await reader.ReadPacketAtAsync(0, 1);
            Assert.True(inspector.IsRandomAccess(first)); Assert.False(inspector.IsRandomAccess(predictive));
            Assert.Throws<InvalidDataException>(() => decoder.TrySend(predictive));
            decoder.Reset(); Assert.Equal(CodecInputStatus.Accepted, decoder.TrySend(first));
        }
    }
}
