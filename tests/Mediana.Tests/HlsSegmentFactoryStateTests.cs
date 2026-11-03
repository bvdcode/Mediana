// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    public class HlsSegmentFactoryStateTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task FailedFactoryFaultsOutputAndItsCapturedStateRemainsReusable(bool cancel)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (CancellationTokenSource cancellation = new())
            {
                VideoFormat format = H264EncoderTests.Format(16, 16);
                using H264Encoder encoder = H264Encoder.Create(format, new(1, 1000), allocator,
                    new() { KeyFrameInterval = TimeSpan.FromMilliseconds(200) });
                MpegTsSegmentState? captured = null;
                HlsTestDestination original = new();
                int calls = 0;
                await using AvcHlsWriter writer = AvcHlsWriter.Create(encoder.Stream, async (number, state, token) =>
                {
                    calls++;
                    if (number == 0)
                    {
                        Assert.Null(state);
                        return original;
                    }
                    Assert.Equal(1, number);
                    captured = state;
                    Assert.NotNull(state);
                    if (cancel)
                    {
                        await cancellation.CancelAsync();
                        token.ThrowIfCancellationRequested();
                    }
                    throw new IOException("Segment destination failure.");
                }, allocator, new() { TargetDuration = new(1, new(1, 5)) });
                using VideoFrame first = H264EncoderTests.Frame(format, allocator, 0);
                encoder.TrySend(first);
                using MediaPacket opening = encoder.Receive().Value!;
                await writer.WritePacketAsync(opening);
                using VideoFrame next = H264EncoderTests.Frame(format, allocator, 5);
                encoder.TrySend(next);
                using MediaPacket packet = encoder.Receive().Value!;
                if (cancel)
                {
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writer.WritePacketAsync(packet, cancellation.Token));
                }
                else
                {
                    await Assert.ThrowsAsync<IOException>(async () => await writer.WritePacketAsync(packet));
                }
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.WritePacketAsync(packet));
                Assert.Throws<InvalidOperationException>(() => writer.CreateVodPlaylist(_ => "segment.ts"));
                Assert.Equal(2, calls);
                await writer.DisposeAsync();
                Assert.True(original.Disposed);
                byte[]? expected = null;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    using MemoryStream output = new();
                    await using (MpegTsWriter resumed = MpegTsWriter.ResumeSegment(output, captured!, allocator))
                    {
                        await resumed.WritePacketAsync(packet);
                        await resumed.CompleteAsync();
                    }
                    Assert.True(output.CanWrite);
                    byte[] actual = output.ToArray();
                    Assert.NotEmpty(actual);
                    if (expected is not null)
                    {
                        Assert.Equal(expected, actual);
                    }
                    expected = actual;
                }
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
