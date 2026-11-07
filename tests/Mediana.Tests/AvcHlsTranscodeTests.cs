// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Execution;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class AvcHlsTranscodeTests
    {
        [Fact]
        public async Task EveryIndependentlyRestartedSegmentMatchesReferencePixelsAndClocks() =>
            await OnDemandHlsTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Mp4"));

        [Fact]
        public async Task ColdRequestsReadOnlyTheRequestedGopAndEncodeExactlyItsFramesInAnyOrder()
        {
            byte[] input = await Mp4TestData.ReadAsync("hls-on-demand.mp4");
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(input, 37);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator,
                new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                source.Reads.Clear();
                long retained = allocator.AllocatedBytes;
                await using AvcHlsTranscodeSession session = Create(reader, allocator);
                Assert.Empty(source.Reads);
                Assert.Equal(retained, allocator.AllocatedBytes);
                using MemoryStream init = new();
                await session.WriteInitializationAsync(init);
                Assert.Empty(source.Reads);
                byte[]? last = null;
                foreach (int number in new[] { 11, 1, 7, 11 })
                {
                    source.Reads.Clear();
                    using MemoryStream fragment = new();
                    HlsMediaSegment segment = await session.WriteSegmentAsync(number, fragment);
                    Assert.Equal(number, segment.Number);
                    Assert.Equal(new MediaTimestamp(number, new(1, 1)), segment.Start);
                    Assert.Equal(fragment.Length, segment.ByteLength);
                    Assert.Equal(retained, allocator.AllocatedBytes);
                    Assert.True(source.Reads.Sum(read => read.Length) < input.Length / 2);
                    Mp4SampleInfo opening = reader.GetSampleInfo(0, number / 2 * 10);
                    Assert.All(source.Reads, read => Assert.True(read.Offset >= opening.Offset));
                    ReadOnlyMemory<byte> run = Mp4TestBoxes.Get(fragment.ToArray(), "moof", "traf", "trun");
                    Assert.Equal(5u, Mp4TestBoxes.UInt32(run, 4));
                    Assert.Equal((ulong)number * 10240, Mp4TestBoxes.UInt64(Mp4TestBoxes.Get(fragment.ToArray(), "moof", "traf", "tfdt"), 4));
                    List<byte[]> expected = await EncodeSequentialWindowAsync(input, number, allocator);
                    ReadOnlyMemory<byte> payload = Mp4TestBoxes.Get(fragment.ToArray(), "mdat");
                    int offset = 0;
                    for (int sample = 0; sample < 5; sample++)
                    {
                        int length = (int)Mp4TestBoxes.UInt32(run, 16 + sample * 16);
                        Assert.Equal(expected[sample], payload.Slice(offset, length).ToArray());
                        offset += length;
                    }
                    Assert.Equal(payload.Length, offset);
                    if (number == 11)
                    {
                        if (last is not null) { Assert.Equal(last, fragment.ToArray()); }
                        last = fragment.ToArray();
                    }
                }
                string playlist = session.CreateVodPlaylist("init.mp4", number => $"{number}.m4s");
                Assert.Contains("#EXT-X-MAP:URI=\"init.mp4\"\n", playlist);
                Assert.Contains("#EXT-X-INDEPENDENT-SEGMENTS\n", playlist);
                Assert.Equal(12, playlist.Split("#EXTINF:1,\n").Length - 1);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public async Task CancellationAndOutputFailuresReleaseAllWorkAndPermitIdenticalRetry(int failure)
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("hls-on-demand.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            await using AvcHlsTranscodeSession session = Create(reader, allocator);
            long retained = allocator.AllocatedBytes;
            using CancellationTokenSource cancellation = new();
            using MpegTsTestOutput failed = new() { FailOnWrite = failure == 1 ? 2 : null, FailOnFlush = failure == 2 };
            if (failure == 0)
            {
                source.AfterRead = cancellation.Cancel;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.WriteSegmentAsync(7, failed, cancellation.Token).AsTask());
                source.AfterRead = null;
                Assert.Empty(failed.Bytes.ToArray());
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => session.WriteSegmentAsync(7, failed).AsTask());
            }
            Assert.Equal(retained, allocator.AllocatedBytes);
            Assert.False(failed.WasDisposed);
            using MemoryStream retry = new(), repeated = new();
            await session.WriteSegmentAsync(7, retry);
            await session.WriteSegmentAsync(7, repeated);
            Assert.Equal(retry.ToArray(), repeated.ToArray());
            Assert.Equal(retained, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task NonFrameAlignedWindowPublishesNothingAndDoesNotRetainCodecState()
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("hls-on-demand.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            long retained = allocator.AllocatedBytes;
            await using AvcHlsTranscodeSession session = AvcHlsTranscodeSession.Create(reader, 0, new H264DecoderFactory(), Format(),
                [new(0, new(1, new(1, 10)), new(10, new(1, 10)))], allocator);
            using MemoryStream output = new();
            await Assert.ThrowsAsync<InvalidDataException>(() => session.WriteSegmentAsync(0, output).AsTask());
            Assert.Empty(output.ToArray());
            Assert.Equal(retained, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ConcurrentRequestsInitializationAndDisposalCannotInterleave()
        {
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("hls-on-demand.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            await using AvcHlsTranscodeSession session = Create(reader, allocator);
            TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using MpegTsTestOutput output = new() { Gate = gate };
            Task active = session.WriteSegmentAsync(11, output).AsTask();
            await output.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using MemoryStream rejected = new();
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.WriteSegmentAsync(0, rejected).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.WriteInitializationAsync(rejected).AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
            Assert.Empty(rejected.ToArray());
            gate.SetResult();
            await active;
        }

        private static AvcHlsTranscodeSession Create(Mp4Reader reader, MediaBufferAllocator allocator) =>
            AvcHlsTranscodeSession.Create(reader, 0, new H264DecoderFactory(), Format(),
                Enumerable.Range(0, 12).Select(number => new HlsVideoWindow(number, new(number, new(1, 1)), new(1, new(1, 1)))).ToArray(), allocator);

        private static VideoFormat Format() => new(64, 48, VideoPixelFormat.Yuv420P8,
            new(ColorPrimaries.Bt709, TransferFunction.Bt709, MatrixCoefficients.Bt709, ColorRange.Limited, ChromaLocation.Left), new(1, 1));

        private static async Task<List<byte[]>> EncodeSequentialWindowAsync(byte[] input, int number, MediaBufferAllocator allocator)
        {
            await using TrackingRandomAccessSource source = new(input);
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            using MediaDecodePipeline<VideoFrame> decoding = new(reader, new H264DecoderFactory().Create(reader.Streams[0], allocator), 0);
            using H264Encoder encoder = H264Encoder.Create(Format(), reader.Streams[0].TimeBase, allocator);
            List<byte[]> packets = [];
            for (int frame = 0; frame < (number + 1) * 5; frame++)
            {
                using VideoFrame decoded = await decoding.ReadAsync() ?? throw new InvalidDataException("The source ended before the selected window.");
                if (frame >= number * 5)
                {
                    Assert.Equal(CodecInputStatus.Accepted, encoder.TrySend(decoded));
                    using MediaPacket packet = encoder.Receive().Value!;
                    packets.Add(packet.Data.ToArray());
                }
            }
            return packets;
        }
    }
}
