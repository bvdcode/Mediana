// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class Mp4FragmentHlsTests
    {
        [Fact]
        public async Task ColdOpenAndInitializationReadNoMediaPayloadThenRequestReadsOnlySelectedWindow()
        {
            byte[] data = await Mp4TestData.ReadAsync("avc-segments.mp4");
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(data, 37);
            List<Mp4SampleInfo> all = [];
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator))
            {
                for (int stream = 0; stream < reader.Tracks.Count; stream++)
                {
                    for (int sample = 0; sample < reader.Tracks[stream].SampleCount; sample++) { all.Add(reader.GetSampleInfo(stream, sample)); }
                }
            }
            source.Reads.Clear();
            await using (Mp4FragmentHlsSession session = await Mp4FragmentHlsSession.OpenAsync(source, allocator, 0, 1, Options()))
            {
                Assert.Equal(3, session.Segments.Count);
                Assert.All(source.Reads, read => Assert.DoesNotContain(all,
                    sample => read.Offset < sample.Offset + sample.Size && read.Offset + read.Length > sample.Offset));
                Assert.True(source.Reads.Sum(read => read.Length) < data.Length / 4);
                source.Reads.Clear();
                using MemoryStream init = new();
                await session.WriteInitializationAsync(init);
                Assert.Empty(source.Reads);
                Assert.True(init.CanWrite);
                Assert.NotEmpty(init.ToArray());
                long retained = allocator.AllocatedBytes;
                byte[]? last = null;
                foreach (int number in new[] { 2, 0, 1, 2 })
                {
                    source.Reads.Clear();
                    Mp4HlsSegment segment = session.Segments[number];
                    Mp4SampleInfo[] selected = all.Where(sample => sample.StreamIndex switch
                    {
                        0 => sample.SampleIndex >= segment.Video.FirstSample && sample.SampleIndex < segment.Video.FirstSample + segment.Video.SampleCount,
                        1 => sample.SampleIndex >= segment.Audio!.Value.FirstSample && sample.SampleIndex < segment.Audio.Value.FirstSample + segment.Audio.Value.SampleCount,
                        _ => false
                    }).ToArray();
                    using MemoryStream output = new();
                    await session.WriteSegmentAsync(number, output);
                    int opening = selected.First(sample => sample.StreamIndex == 0).Size;
                    Assert.Equal(selected.Sum(sample => sample.Size) + opening, source.Reads.Sum(read => read.Length));
                    Assert.All(source.Reads, read => Assert.Contains(selected,
                        sample => read.Offset >= sample.Offset && read.Offset + read.Length <= sample.Offset + sample.Size));
                    Assert.Equal(retained, allocator.AllocatedBytes);
                    if (number == 2)
                    {
                        if (last is not null) { Assert.Equal(last, output.ToArray()); }
                        last = output.ToArray();
                    }
                }
                string playlist = session.CreateVodPlaylist("init.mp4", number => $"{number}.m4s");
                Assert.Contains("#EXT-X-VERSION:7\n", playlist);
                Assert.Contains("#EXT-X-MAP:URI=\"init.mp4\"\n", playlist);
                Assert.DoesNotContain("#EXT-X-DISCONTINUITY", playlist);
                Assert.Throws<ArgumentException>(() => session.CreateVodPlaylist("bad\ninit.mp4", _ => "0.m4s"));
                Assert.Throws<ArgumentException>(() => session.CreateVodPlaylist("", _ => "0.m4s"));
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task FailedOrCancelledRequestReleasesBuffersAndCanBeRetried(bool cancel)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"), 31);
            await using Mp4FragmentHlsSession session = await Mp4FragmentHlsSession.OpenAsync(source, allocator, 0, 1, Options());
            long retained = allocator.AllocatedBytes;
            using CancellationTokenSource cancellation = new();
            using MemoryStream failed = new();
            if (cancel)
            {
                source.AfterRead = cancellation.Cancel;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.WriteSegmentAsync(2, failed, cancellation.Token).AsTask());
            }
            else
            {
                source.AfterRead = () => throw new IOException("Source read failed.");
                await Assert.ThrowsAsync<IOException>(() => session.WriteSegmentAsync(2, failed).AsTask());
            }
            source.AfterRead = null;
            Assert.Equal(retained, allocator.AllocatedBytes);
            using MemoryStream retry = new(), repeated = new();
            await session.WriteSegmentAsync(2, retry);
            await session.WriteSegmentAsync(2, repeated);
            Assert.Equal(retry.ToArray(), repeated.ToArray());
        }

        [Fact]
        public async Task ConcurrentInitializationReadAndDisposalAreRejectedWhileFragmentIsWriting()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            await using Mp4FragmentHlsSession session = await Mp4FragmentHlsSession.OpenAsync(source, allocator, 0, options: Options());
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using MpegTsTestOutput output = new() { Gate = gate };
            Task active = session.WriteSegmentAsync(1, output).AsTask();
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using MemoryStream rejected = new();
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.WriteInitializationAsync(rejected).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.WriteSegmentAsync(0, rejected).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
            gate.SetResult();
            await active;
            Assert.Empty(rejected.ToArray());
            Assert.NotEmpty(output.Bytes.ToArray());
        }

        [Fact]
        public async Task InvalidSelectionReleasesReaderWithoutReadingMediaOrClosingSource()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-segments.mp4"));
            await Assert.ThrowsAsync<ArgumentException>(() => Mp4FragmentHlsSession.OpenAsync(source, allocator, 0, 0).AsTask());
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Mp4FragmentHlsSession.OpenAsync(source, allocator, -1).AsTask());
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        private static Mp4FragmentHlsOptions Options() => new() { Planning = new() { TargetDuration = new(9, new TimeBase(1, 4)) } };
    }
}
