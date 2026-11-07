// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Codecs.Vp9;
using Mediana.Containers.Matroska;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Execution;
using Mediana.IO;

namespace Mediana.Tests
{
    public class VideoTranscodeTests
    {
        [Fact]
        public async Task AdaptiveVp9ConvenienceUsesTheSamePreparationClockAndOutputAsProcessing()
        {
            byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Vp9", "resize.webm"));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new MatroskaReaderFactory())
                .AddVideoDecoder(new Vp9DecoderFactory()).AddVideoEncoder(new H264EncoderFactory())
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes, 7);
            MediaDurationAccumulator expected = new();
            await using (MatroskaReader reader = await MatroskaReader.OpenAsync(source, allocator))
            {
                Assert.Equal(20, reader.GetPresentationUnitCount(0));
                for (long index = 0; index < reader.GetPresentationUnitCount(0); index++)
                { expected.Add(reader.GetPresentationUnit(0, index).Duration); }
                Assert.Equal(new MediaDuration(83_333_333, new(1, 1_000_000_000)), reader.GetPresentationUnit(0, 19).Duration);
            }
            await VerifyAsync(engine, source, allocator, 20, expected.Duration!.Value);
        }

        [Fact]
        public async Task FractionalMovieTailDerivesAnExactClockInsteadOfRoundingToTheStreamClock()
        {
            byte[] bytes = Mp4EditTestData.Replace(await Mp4TestData.ReadAsync("avc-tail.mov"), false, (1101, 0, 65536));
            MediaEngine engine = new MediaEngineBuilder()
                .AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).AddVideoEncoder(new H264EncoderFactory())
                .AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(bytes, 7);
            await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
            {
                Assert.Equal(new TimeBase(1, 10240), reader.Streams[0].TimeBase);
                Assert.Equal(new MediaTimestamp(1101, new(1, 1000)), reader.GetPresentationRange(0)!.Value.End);
            }
            await VerifyAsync(engine, source, allocator, 6, new(1101, new(1, 1000)));
        }

        private static async Task VerifyAsync(MediaEngine engine, IByteSource source, MediaBufferAllocator allocator,
            long frames, MediaDuration duration)
        {
            using MemoryStream output = new();
            List<MediaOutputProgress> updates = [];
            VideoTranscodeOptions options = new() { MaximumWidth = 32 };
            MediaOperationProgress result = await engine.TranscodeVideoAsync(source, output, "h264", "mpegts", allocator, options, updates.Add);
            Assert.Equal(frames, result.Frames);
            Assert.Equal(0, new MediaTimestamp(duration.Value, duration.TimeBase).CompareTo(new(result.ProcessedDuration!.Value.Value, result.ProcessedDuration.Value.TimeBase)));
            Assert.True(result.IsComplete);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.NotEmpty(updates);
            Assert.All(updates.Take(updates.Count - 1), update => Assert.False(update.IsComplete));
            Assert.True(updates[^1].IsComplete);
            Assert.Equal(result.ProcessedDuration, updates[^1].ProcessedDuration);
            Assert.Equal(output.Length, updates[^1].BytesWritten);
            for (int index = 1; index < updates.Count; index++) { Assert.True(updates[index].BytesWritten >= updates[index - 1].BytesWritten); }
            using MemoryStream reference = new();
            MediaProcessingResult processed = await engine.ProcessAsync(source, reference, "mpegts",
                [new VideoEncodingRequest(0, 0, new H264EncoderFactory(), options)], allocator);
            Assert.Equal(output.ToArray(), reference.ToArray());
            Assert.Equal(result.Frames, processed.Tracks[0].Frames);
            Assert.Equal(result.Packets, processed.Packets);
            Assert.Equal(result.PayloadBytes, processed.Tracks[0].PayloadBytes);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.True(output.CanWrite);
        }
    }
}
