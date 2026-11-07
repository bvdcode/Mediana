// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Containers.Mp4;
using Mediana.Containers.MpegTs;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    public class ProcessingAllocatorTests
    {
        [Theory]
        [InlineData("indexed", 1)]
        [InlineData("processing", 16384)]
        [InlineData("convenience", 16384)]
        public async Task DecoderInputLimitIsEnforcedIndependentlyOfTheLargerOutputBudget(string operation, int limit)
        {
            ObservedVideoDecoderFactory decoder = new(new H264DecoderFactory());
            ObservedContainerFactory container = new(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }));
            MediaEngine engine = new MediaEngineBuilder().AddContainer(container).AddVideoDecoder(decoder)
                .AddVideoEncoder(new H264EncoderFactory()).AddContainerWriter(new MpegTsWriterFactory()).Build();
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            await using TrackingRandomAccessSource source = new(await Mp4TestData.ReadAsync("avc-tail.mov"));
            using MemoryStream output = new();
            MediaOpenOptions input = new() { BufferBudgetBytes = limit };
            switch (operation)
            {
                case "indexed":
                    await using (Mp4Reader reader = await Mp4Reader.OpenAsync(source, allocator, new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                    {
                        long baseline = allocator.AllocatedBytes;
                        await Assert.ThrowsAsync<MediaMemoryLimitException>(async () =>
                        {
                            await using MediaProcessingSession session = await engine.OpenProcessingAsync(reader,
                                [new VideoEncodingRequest(0, 0, new H264EncoderFactory())], allocator, new() { Input = input });
                        });
                        Assert.Equal(baseline, allocator.AllocatedBytes);
                    }
                    break;
                case "processing":
                    await Assert.ThrowsAsync<MediaMemoryLimitException>(() => engine.ProcessAsync(source, output, "mpegts",
                        [new VideoEncodingRequest(0, 0, new H264EncoderFactory())], allocator, new() { Input = input }));
                    Assert.Same(container.Allocator, decoder.Allocator);
                    break;
                case "convenience":
                    await Assert.ThrowsAsync<MediaMemoryLimitException>(() => engine.TranscodeVideoAsync(source, output,
                        "h264", "mpegts", allocator, new() { Input = input }));
                    Assert.Same(container.Allocator, decoder.Allocator);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
            Assert.NotNull(decoder.Allocator);
            Assert.NotSame(allocator, decoder.Allocator);
            Assert.Equal(limit, decoder.Allocator.LimitBytes);
            Assert.True(decoder.Allocator.IsWithin(allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(0, output.Length);
            Assert.True(output.CanWrite);
            Assert.False(source.IsDisposed);
        }
    }
}
