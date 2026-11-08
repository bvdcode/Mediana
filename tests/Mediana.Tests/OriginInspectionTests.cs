// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Aac;
using Mediana.Containers;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Execution;

namespace Mediana.Tests
{
    public class OriginInspectionTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task OpeningInspectionIsBoundedReusedAndPreservesContainerCursors(bool preroll, bool needsOrigin)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await DataAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            IIndexedContainerReader indexed = preroll ? reader : new IndexedReaderView(reader);
            OriginInspectorProbe inspector = new(_ => new RandomAccessRecovery(0), needsOrigin,
                packet => Assert.Equal(reader.GetSampleInfo(0, 0).PresentationTime, packet.PresentationTime));
            using MediaDecodePipeline<AudioFrame> pipeline = new(indexed,
                new AacDecoderFactory().Create(reader.Streams[0], allocator), 0, randomAccessInspector: inspector);
            source.Reads.Clear();
            foreach (int packet in new[] { 10, 4, 10 })
            {
                await pipeline.SeekAsync(reader.GetSampleInfo(0, packet).PresentationTime);
                using AudioFrame frame = (await pipeline.ReadAsync())!;
                Assert.Equal(reader.GetSampleInfo(0, packet).PresentationTime, frame.Timestamp);
            }
            Assert.Equal(needsOrigin ? 1 : 0, inspector.OriginReads);
            Assert.Equal(needsOrigin ? 1 : 0, source.Reads.Count(range => range.Offset == reader.GetSampleInfo(0, 0).Offset));
            if (needsOrigin)
            {
                Assert.Throws<ObjectDisposedException>(() => inspector.Origin!.Data);
            }
            using (MediaPacket packet = (await reader.ReadPacketAsync())!)
            {
                Assert.Equal(reader.GetSampleInfo(0, 0).PresentationTime, packet.PresentationTime);
            }
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailureAndCancellationDoNotPublishPartialInitialization(bool cancel)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await DataAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            using CancellationTokenSource cancellation = new();
            OriginInspectorProbe inspector = new(_ => new RandomAccessRecovery(0), inspectOrigin: _ =>
            {
                if (cancel)
                {
                    cancellation.Cancel();
                    return;
                }
                throw new InvalidDataException("Invalid opening signaling.");
            });
            using MediaDecodePipeline<AudioFrame> pipeline = new(reader,
                new AacDecoderFactory().Create(reader.Streams[0], allocator), 0, randomAccessInspector: inspector);
            Task seek = pipeline.SeekAsync(reader.GetSampleInfo(0, 10).PresentationTime, cancellation.Token).AsTask();
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seek);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => seek);
            }
            Assert.Equal(1, inspector.OriginReads);
            Assert.Empty(inspector.Inspections);
            Assert.Throws<ObjectDisposedException>(() => inspector.Origin!.Data);
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ReadAsync().AsTask());
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task OriginRecoveryCannotAuthorizeAHeaderlessLaterRestart()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await DataAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            OriginInspectorProbe inspector = new(_ => null, originRecovery: new(0));
            using MediaDecodePipeline<AudioFrame> pipeline = new(new IndexedReaderView(reader),
                new AacDecoderFactory().Create(reader.Streams[0], allocator), 0, randomAccessInspector: inspector);
            await pipeline.SeekAsync(reader.GetSampleInfo(0, 10).PresentationTime);
            using (AudioFrame frame = (await pipeline.ReadAsync())!)
            {
                Assert.Equal(reader.GetSampleInfo(0, 0).PresentationTime, frame.Timestamp);
            }
            Assert.NotEmpty(inspector.Inspections);
            Assert.Equal(1, inspector.OriginReads);
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ValidatedOriginStartsWithoutChangingOrdinaryRestartInspection(bool preroll)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await DataAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            IIndexedContainerReader indexed = preroll ? reader : new IndexedReaderView(reader);
            OriginInspectorProbe inspector = new(_ => null, originRecovery: new(0));
            using MediaDecodePipeline<AudioFrame> pipeline = new(indexed,
                new AacDecoderFactory().Create(reader.Streams[0], allocator), 0, randomAccessInspector: inspector);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                await pipeline.SeekAsync(reader.GetSampleInfo(0, 0).PresentationTime);
                using AudioFrame frame = (await pipeline.ReadAsync())!;
                Assert.Equal(reader.GetSampleInfo(0, 0).PresentationTime, frame.Timestamp);
            }
            Assert.Empty(inspector.Inspections);
            Assert.Equal(1, inspector.OriginReads);
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task OriginRecoveryDoesNotDiscardDecoderPrerollRequirements()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await DataAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            OriginInspectorProbe inspector = new(_ => null, originRecovery: new(1));
            using MediaDecodePipeline<AudioFrame> pipeline = new(new IndexedReaderView(reader),
                new AacDecoderFactory().Create(reader.Streams[0], allocator), 0, randomAccessInspector: inspector);
            await Assert.ThrowsAsync<NotSupportedException>(() => pipeline.SeekAsync(reader.GetSampleInfo(0, 0).PresentationTime).AsTask());
            Assert.Equal(1, inspector.OriginReads);
            Assert.Empty(inspector.Inspections);
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task OriginReadConsumesTheSamePacketBudgetAsSeekAndDecode()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await DataAsync());
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator);
            OriginInspectorProbe inspector = new(_ => new RandomAccessRecovery(0));
            using MediaDecodePipeline<AudioFrame> pipeline = new(reader,
                new AacDecoderFactory().Create(reader.Streams[0], allocator), 0, maxPacketReads: 1, randomAccessInspector: inspector);
            await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.SeekAsync(reader.GetSampleInfo(0, 10).PresentationTime).AsTask());
            Assert.Equal(1, inspector.OriginReads);
            Assert.Empty(inspector.Inspections);
            pipeline.Dispose();
            await reader.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static Task<byte[]> DataAsync() => File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Aac", "lc-mono-48.m4a"));
    }
}
