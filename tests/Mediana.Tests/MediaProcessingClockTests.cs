// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;

namespace Mediana.Tests
{
    public class MediaProcessingClockTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExplicitClockSupportsIndexedReadersWithoutMetadataClockQueries(bool explicitClock)
        {
            MediaBufferAllocator allocator = new(8 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("hls-on-demand.mp4"));
            await using Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie });
            await using IndexedReaderView view = new(reader);
            MediaEngine engine = new MediaEngineBuilder().AddVideoDecoder(new H264DecoderFactory()).Build();
            long baseline = allocator.AllocatedBytes;
            source.Reads.Clear();
            async Task RunAsync()
            {
                await using MediaProcessingSession session = await engine.OpenProcessingAsync(view,
                    [new VideoEncodingRequest(0, 7, new H264EncoderFactory(), outputTimeBase: explicitClock ? new TimeBase(1, 5) : null)], allocator,
                    new() { Selection = new(new(11, new(1, 1)), new(12, new(1, 1))) });
                await using ProcessingPacketWriter writer = new();
                MediaProcessingResult result = await session.WriteAsync(writer);
                Assert.Equal(5, result.Tracks[0].Frames);
                Assert.Equal(new TimeBase(1, 5), session.Streams[0].TimeBase);
            }
            if (explicitClock) { await RunAsync(); }
            else
            {
                await Assert.ThrowsAsync<NotSupportedException>(RunAsync);
                Assert.Empty(source.Reads);
            }
            Assert.Equal(baseline, allocator.AllocatedBytes);
        }
    }
}
