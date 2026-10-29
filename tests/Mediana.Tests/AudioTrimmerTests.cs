using System.Runtime.InteropServices;
using Mediana.Core.Audio;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Processing.Audio;

namespace Mediana.Tests
{
    public class AudioTrimmerTests
    {
        [Theory]
        [InlineData(AudioStorageLayout.Interleaved, false)]
        [InlineData(AudioStorageLayout.Interleaved, true)]
        [InlineData(AudioStorageLayout.Planar, false)]
        [InlineData(AudioStorageLayout.Planar, true)]
        public async Task BothBoundariesPreserveEveryChannelAndIndependentOutput(AudioStorageLayout layout, bool rebase)
        {
            MediaBufferAllocator allocator = new(4096);
            AudioSignalSource source = new(allocator, 48000, 40, (index, channel) => index + channel * 100,
                [3, 7, 5], layout, ["left", "right"]);
            MediaTimestamp? origin = rebase ? new MediaTimestamp(1, new TimeBase(1, 7)) : null;
            await using AudioTrimmer trimmer = new(source.ReadAsync, 6, 17, allocator, origin);
            int position = 0;
            AudioFrame? retained = null;
            try
            {
                while (true)
                {
                    using AudioFrame? frame = await trimmer.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                    Assert.Equal(layout, frame.Format.Layout);
                    Assert.Equal(new[] { "left", "right" }, frame.Format.Channels);
                    for (int sample = 0; sample < frame.SamplesPerChannel; sample++)
                    {
                        for (int channel = 0; channel < 2; channel++)
                        {
                            Assert.Equal(6 + position + sample + channel * 100, PcmSampleAccess.Read(frame, sample, channel));
                        }
                    }
                    MediaTimestamp timestamp = rebase
                        ? new MediaTimestamp(48000 + position * 7, new TimeBase(1, 336000))
                        : new MediaTimestamp(position - 1, new TimeBase(1, 48000));
                    Assert.Equal(timestamp, frame.Timestamp);
                    Assert.Equal(new MediaDuration(frame.SamplesPerChannel, new TimeBase(1, 48000)), frame.Duration);
                    position += frame.SamplesPerChannel;
                    retained?.Dispose();
                    retained = frame.Retain();
                }
                Assert.Equal(17, position);
                int blocks = source.BlocksRead;
                Assert.Null(await trimmer.ReadFrameAsync());
                Assert.Equal(blocks, source.BlocksRead);
                await trimmer.DisposeAsync();
                Assert.NotNull(retained);
                Assert.NotEmpty(retained.GetPlane(0).ToArray());
                Assert.Equal(retained.Format.GetBufferSize(retained.SamplesPerChannel), allocator.AllocatedBytes);
            }
            finally
            {
                retained?.Dispose();
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            using AudioFrame next = (await source.ReadAsync(default))!;
            Assert.NotNull(next);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ThroughEofCanPreserveUnknownTimeOrAssignAnExplicitOrigin(bool rebase)
        {
            MediaBufferAllocator allocator = new(4096);
            AudioSignalSource source = new(allocator, 8000, 11, (index, _) => index, [4]) { Timestamp = _ => null };
            await using AudioTrimmer trimmer = new(source.ReadAsync, 3, null, allocator,
                rebase ? new MediaTimestamp(-2, new TimeBase(1, 8000)) : null);
            int position = 0;
            while (true)
            {
                using AudioFrame? frame = await trimmer.ReadFrameAsync();
                if (frame is null)
                {
                    break;
                }
                Assert.Equal(rebase ? new MediaTimestamp(position - 2, new TimeBase(1, 8000)) : null, frame.Timestamp);
                Assert.Equal((double)(position + 3), MemoryMarshal.Read<double>(frame.GetPlane(0).Span));
                position += frame.SamplesPerChannel;
            }
            Assert.Equal(8, position);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(11L, null)]
        [InlineData(2L, 9L)]
        public async Task ARequestedRangeThatDoesNotExistFailsAndReleasesInput(long skip, long? count)
        {
            MediaBufferAllocator allocator = new(4096);
            AudioSignalSource source = new(allocator, 8000, 10, (index, _) => index, [4]);
            await using AudioTrimmer trimmer = new(source.ReadAsync, skip, count, allocator);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                while (true)
                {
                    using AudioFrame? frame = await trimmer.ReadFrameAsync();
                    if (frame is null)
                    {
                        break;
                    }
                }
            });
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await trimmer.ReadFrameAsync());
        }

        [Fact]
        public async Task EmptySelectionDoesNotReadAndCancellationAfterReadReleasesTheBorrowedFrame()
        {
            MediaBufferAllocator allocator = new(4096);
            AudioSignalSource source = new(allocator, 8000, 10, (index, _) => index, [4]);
            await using (AudioTrimmer empty = new(source.ReadAsync, 100, 0, allocator))
            {
                Assert.Null(await empty.ReadFrameAsync());
                Assert.Equal(0, source.BlocksRead);
            }
            using CancellationTokenSource cancellation = new();
            async ValueTask<AudioFrame?> ReadAsync(CancellationToken token)
            {
                AudioFrame? frame = await source.ReadAsync(token);
                await cancellation.CancelAsync();
                return frame;
            }
            await using AudioTrimmer canceled = new(ReadAsync, 1, 2, allocator);
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await canceled.ReadFrameAsync(cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await canceled.ReadFrameAsync());
        }

        [Fact]
        public async Task DiscontinuousTimestampAndAllocationFailureAreTerminalWithoutInputLeaks()
        {
            MediaBufferAllocator inputAllocator = new(4096);
            MediaBufferAllocator tooSmall = new(1);
            AudioSignalSource source = new(inputAllocator, 8000, 10, (index, _) => index, [4]);
            await using (AudioTrimmer limited = new(source.ReadAsync, 1, 2, tooSmall))
            {
                await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await limited.ReadFrameAsync());
                Assert.Equal(0, inputAllocator.AllocatedBytes);
                Assert.Equal(0, tooSmall.AllocatedBytes);
            }
            source.Timestamp = index => new MediaTimestamp(index * 2, new TimeBase(1, 8000));
            await using AudioTrimmer discontinuous = new(source.ReadAsync, 4, null, inputAllocator);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await discontinuous.ReadFrameAsync());
            Assert.Equal(0, inputAllocator.AllocatedBytes);
        }
    }
}
