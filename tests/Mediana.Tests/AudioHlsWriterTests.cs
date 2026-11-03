// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Formats;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class AudioHlsWriterTests
    {
        [Theory]
        [InlineData("avc-aac.mp4", "mp4a.40.2", 1024)]
        [InlineData("avc-mp3-44.mp4", "mp4a.6B", 1152)]
        [InlineData("avc-mp3-22.mp4", "mp4a.69", 576)]
        public Task RealAudioPreservesAllPacketsAndUsesOneExactSegmentGrid(string file, string codecs, int frameSamples) =>
            AudioHlsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"), file, codecs, frameSamples);

        [Theory]
        [InlineData(0)]
        [InlineData(1023)]
        [InlineData(1025)]
        public async Task GapOverlapOrRepeatedTimeFaultsWithoutAppendingOrOpeningOutput(int start)
        {
            MediaBufferAllocator allocator = new(4096);
            int opened = 0;
            HlsTestDestination destination = new();
            await using AudioHlsWriter writer = AudioHlsWriter.Create(Audio(), (_, _) =>
            {
                opened++;
                return ValueTask.FromResult<Stream>(destination);
            }, allocator, new() { TargetDuration = new(1024, new(1, 48000)) });
            using MediaPacket first = Packet(allocator, 0);
            using MediaPacket invalid = Packet(allocator, start);
            await writer.WritePacketAsync(first);
            long bytes = writer.BytesWritten;
            await Assert.ThrowsAsync<InvalidDataException>(() => writer.WritePacketAsync(invalid).AsTask());
            Assert.Equal(bytes, writer.BytesWritten);
            Assert.Equal(1, opened);
            Assert.Equal(1, invalid.Data.Length);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            await writer.DisposeAsync();
            Assert.True(destination.Disposed);
            first.Dispose();
            invalid.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task SegmentLimitRejectsTheNextDestinationBeforeOpeningIt()
        {
            MediaBufferAllocator allocator = new(4096);
            int opened = 0;
            HlsTestDestination destination = new();
            await using AudioHlsWriter writer = AudioHlsWriter.Create(Audio(), (_, _) =>
            {
                opened++;
                return ValueTask.FromResult<Stream>(destination);
            }, allocator, new() { TargetDuration = new(1024, new(1, 48000)), MaximumSegments = 1 });
            using MediaPacket first = Packet(allocator, 0);
            using MediaPacket boundary = Packet(allocator, 1024);
            await writer.WritePacketAsync(first);
            long bytes = writer.BytesWritten;
            await Assert.ThrowsAsync<NotSupportedException>(() => writer.WritePacketAsync(boundary).AsTask());
            Assert.Equal(1, opened);
            Assert.Equal(bytes, writer.BytesWritten);
            await writer.DisposeAsync();
            Assert.True(destination.Disposed);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task EmptyOrPreCancelledInputNeverOpensOutput(bool cancel)
        {
            MediaBufferAllocator allocator = new(4096);
            int opened = 0;
            await using AudioHlsWriter writer = AudioHlsWriter.Create(Audio(), (_, _) =>
            {
                opened++;
                return ValueTask.FromResult<Stream>(new MemoryStream());
            }, allocator);
            if (cancel)
            {
                using MediaPacket packet = Packet(allocator, 0);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WritePacketAsync(packet, new(true)).AsTask());
            }
            else
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            }
            Assert.Equal(0, opened);
            Assert.False(writer.IsComplete);
            Assert.Throws<InvalidOperationException>(() => writer.CreateVodPlaylist(_ => "audio.ts"));
            await writer.DisposeAsync();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task OverlappingCallsCannotDisposeOrCompleteTheActiveOutput()
        {
            MediaBufferAllocator allocator = new(4096);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            HlsTestDestination destination = new() { ReleaseWrites = release };
            await using AudioHlsWriter writer = AudioHlsWriter.Create(Audio(), (_, _) => ValueTask.FromResult<Stream>(destination), allocator);
            using MediaPacket packet = Packet(allocator, 0);
            Task pending = writer.WritePacketAsync(packet).AsTask();
            await destination.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.DisposeAsync().AsTask());
            release.SetResult();
            await pending;
            await writer.CompleteAsync();
            Assert.Single(writer.Segments);
            Assert.True(destination.Disposed);
        }

        [Theory]
        [InlineData(4, 0, 0, 1024, 48000)]
        [InlineData(3, null, 0, 1024, 48000)]
        [InlineData(3, 0, null, 1024, 48000)]
        [InlineData(3, 0, 1, 1024, 48000)]
        [InlineData(3, 1, 1, 1024, 44101)]
        public async Task UnknownOrInexactPacketTimingCannotOpenOutput(int stream, int? pts, int? dts, int? duration, int denominator)
        {
            MediaBufferAllocator allocator = new(4096);
            int opened = 0;
            await using AudioHlsWriter writer = AudioHlsWriter.Create(Audio(), (_, _) =>
            {
                opened++;
                return ValueTask.FromResult<Stream>(new MemoryStream());
            }, allocator);
            using WritableMediaBuffer storage = allocator.Allocate(1);
            storage.Span.Clear();
            using MediaBuffer buffer = storage.Publish();
            TimeBase clock = new(1, denominator);
            using MediaPacket packet = new(stream, buffer, pts.HasValue ? new(pts.Value, clock) : null,
                dts.HasValue ? new(dts.Value, clock) : null, duration.HasValue ? new(duration.Value, clock) : null);
            await Assert.ThrowsAsync<NotSupportedException>(() => writer.WritePacketAsync(packet).AsTask());
            Assert.Equal(0, opened);
            Assert.Equal(0, writer.BytesWritten);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void InvalidOptionsFailBeforeCallingTheDestinationFactory(int scenario)
        {
            HlsWriteOptions options = scenario switch
            {
                0 => new() { TargetDuration = new(0, new(1, 1)) },
                1 => new() { TargetDuration = default },
                2 => new() { Transport = null! },
                3 => new() { MaximumSegments = 0 },
                4 => new() { MaximumSegmentUriCharacters = 0 },
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            MediaBufferAllocator allocator = new(4096);
            Assert.ThrowsAny<ArgumentException>(() => AudioHlsWriter.Create(Audio(),
                (_, _) => throw new InvalidOperationException("No destination should be opened."), allocator, options));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InvalidLaterDestinationIsDisposedAndThePresentationCannotComplete()
        {
            MediaBufferAllocator allocator = new(4096);
            HlsTestDestination first = new();
            using MemoryStream readOnly = new([0], writable: false);
            await using AudioHlsWriter writer = AudioHlsWriter.Create(Audio(), (number, _) =>
                ValueTask.FromResult<Stream>(number == 0 ? first : readOnly), allocator,
                new() { TargetDuration = new(1024, new(1, 48000)) });
            using MediaPacket initial = Packet(allocator, 0);
            using MediaPacket next = Packet(allocator, 1024);
            await writer.WritePacketAsync(initial);
            long bytes = writer.BytesWritten;
            await Assert.ThrowsAsync<ArgumentException>(() => writer.WritePacketAsync(next).AsTask());
            Assert.False(readOnly.CanRead);
            Assert.Equal(bytes, writer.BytesWritten);
            Assert.Empty(writer.Segments);
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync().AsTask());
            await writer.DisposeAsync();
            Assert.True(first.Disposed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        [InlineData(513)]
        public async Task CompleteCodedDurationDoesNotApplyContainerPresentationClipping(int? declaredSamples)
        {
            MediaBufferAllocator allocator = new(4096);
            HlsTestDestination destination = new();
            await using AudioHlsWriter writer = AudioHlsWriter.Create(Audio(), (_, _) => ValueTask.FromResult<Stream>(destination), allocator);
            using MediaPacket original = Packet(allocator, 0);
            using MediaPacket packet = original.RetainWithDuration(declaredSamples.HasValue
                ? new(declaredSamples.Value, new(1, 48000)) : null);
            await writer.WritePacketAsync(packet);
            await writer.CompleteAsync();
            Assert.Equal(new MediaDuration(1024, new(1, 48000)), Assert.Single(writer.Segments).Duration);
            Assert.DoesNotContain("#EXT-X-INDEPENDENT-SEGMENTS", writer.CreateVodPlaylist(_ => "audio.ts"));
        }

        [Fact]
        public void ClockMustRepresentWholeCodedFramesBeforeOpeningOutput()
        {
            MediaBufferAllocator allocator = new(4096);
            EncodedStreamInfo rounded = new(3, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 1000), [0x11, 0x88]);
            Assert.Throws<NotSupportedException>(() => AudioHlsWriter.Create(rounded,
                (_, _) => throw new InvalidOperationException("No destination should be opened."), allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        private static EncodedStreamInfo Audio() => new(3, MediaKind.Audio, "aac", StreamFormats.Aac, new(1, 48000), [0x11, 0x88]);

        private static MediaPacket Packet(MediaBufferAllocator allocator, long start)
        {
            using WritableMediaBuffer storage = allocator.Allocate(1);
            storage.Span.Clear();
            using MediaBuffer buffer = storage.Publish();
            MediaTimestamp time = new(start, new(1, 48000));
            return new(3, buffer, time, time, new(1024, time.TimeBase));
        }
    }
}
