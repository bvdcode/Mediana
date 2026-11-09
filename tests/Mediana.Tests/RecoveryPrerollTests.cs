// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers.Mp4;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class RecoveryPrerollTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(-5)]
        public async Task InspectionRecoveryAndContainerRollPreserveTheRequestedOutput(short containerRoll)
        {
            byte[] bytes = await DataAsync();
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(Mp4TestData.PayloadOffset(bytes, "sgpd") + 16), containerRoll);
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            RecoveryInspectorProbe inspector = new(packet => packet.PresentationTime!.Value.Value / 1024 % 2 == 1
                ? new RandomAccessRecovery(3) : null);
            using MediaDecodePipeline<AudioFrame> pipeline = new(reader,
                new AacDecoderFactory().Create(reader.Streams[0], allocator), 0, randomAccessInspector: inspector);
            source.Reads.Clear();
            await pipeline.SeekAsync(reader.GetSampleInfo(0, 10).PresentationTime);
            long[] inspected = inspector.Inspections.Select(time => time!.Value.Value / 1024).ToArray();
            Assert.Equal(containerRoll == -1 ? new long[] { 9, 8, 7 } : [5], inspected);
            using AudioFrame frame = (await pipeline.ReadAsync())!;
            Assert.Equal(reader.GetSampleInfo(0, 10).PresentationTime, frame.Timestamp);
            long restartOffset = reader.GetSampleInfo(0, (int)inspected[^1]).Offset;
            Assert.Single(source.Reads, range => range.Offset == restartOffset);
            frame.Dispose();
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task MissingRestartAndInspectionCancellationReleaseAllStorage(bool cancel)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await DataAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            using CancellationTokenSource cancellation = new();
            RecoveryInspectorProbe inspector = new(_ =>
            {
                if (cancel)
                {
                    cancellation.Cancel();
                    return new RandomAccessRecovery(0);
                }
                return null;
            });
            using MediaDecodePipeline<AudioFrame> pipeline = new(reader,
                new AacDecoderFactory().Create(reader.Streams[0], allocator), 0, randomAccessInspector: inspector);
            Task seek = pipeline.SeekAsync(reader.GetSampleInfo(0, 10).PresentationTime, cancellation.Token).AsTask();
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seek);
                Assert.Single(inspector.Inspections);
            }
            else
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => seek);
                Assert.Equal(10, inspector.Inspections.Count);
                Assert.Equal(reader.GetSampleInfo(0, 0).PresentationTime, inspector.Inspections[^1]);
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ReadAsync().AsTask());
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public void RecoveryRejectsNegativeHistoryAndAllowsAnIndependentPacket()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RandomAccessRecovery(-1));
            Assert.Equal(0, new RandomAccessRecovery(0).RequiredPrerollPackets);
        }

        [Fact]
        public async Task RecoveryCannotSilentlyOmitUnsupportedContainerPreroll()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await DataAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            RecoveryInspectorProbe inspector = new(_ => new RandomAccessRecovery(2));
            using MediaDecodePipeline<AudioFrame> pipeline = new(new IndexedReaderView(reader),
                new AacDecoderFactory().Create(reader.Streams[0], allocator), 0, randomAccessInspector: inspector);
            await Assert.ThrowsAsync<NotSupportedException>(() => pipeline.SeekAsync(reader.GetSampleInfo(0, 10).PresentationTime).AsTask());
            Assert.Single(inspector.Inspections);
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static Task<byte[]> DataAsync() => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.m4a"));
    }
}
