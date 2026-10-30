// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;
using Mediana.IO;

namespace Mediana.Tests
{
    public class TgsImageDecoderTests
    {
        private static byte[] PartialComposition => "{\"w\":2,\"h\":1,\"fr\":4,\"ip\":10,\"op\":11.5,\"layers\":[]}"u8.ToArray();

        [Fact]
        public async Task ExactElapsedTimeRespectsNonzeroInPointAndPartialFinalDuration()
        {
            byte[] encoded = PartialComposition;
            MediaBufferAllocator allocator = new(64);
            TgsImageDecoder decoder = new();
            MediaTimestamp time = new(1, new TimeBase(1, 4));
            using (VideoFrame frame = await decoder.DecodeAsync(encoded, new() { Time = time }, allocator))
            {
                Assert.Equal(time, frame.Timestamp);
                Assert.Equal(new MediaDuration(1_250_000, TimeBase.TimeSpanTicks), frame.Duration);
                Assert.Equal(2, frame.Format.Width);
                Assert.Equal(1, frame.Format.Height);
                Assert.Equal(SampleAspectRatio.Square, frame.Format.SampleAspectRatio);
                Assert.Equal(VideoColorInfo.Srgb, frame.Format.Color);
            }
            using (VideoFrame first = await decoder.DecodeAsync(encoded, new(), allocator))
            {
                Assert.Equal(new MediaTimestamp(0, TimeBase.TimeSpanTicks), first.Timestamp);
                Assert.Equal(new MediaDuration(2_500_000, TimeBase.TimeSpanTicks), first.Duration);
            }
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await decoder.DecodeAsync(encoded,
                new() { Time = new MediaTimestamp(3, new TimeBase(1, 8)) }, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData("geometry.json")]
        [InlineData("geometry.tgs")]
        public async Task CommonPipelineMatchesElapsedRenderingWithShortSequentialReads(string name)
        {
            byte[] input = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Tgs", name));
            SvgRenderOptions options = new() { Width = 32, Height = 32, Supersampling = 1 };
            byte[] expected = TgsRenderer.RenderAtTime(TgsReader.Read(input), .25, options).Pixels.ToArray();
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new TgsImageDecoder(renderOptions: options)).Build();
            MediaBufferAllocator allocator = new(64 * 1024);
            await using ShortReadSource source = new(input);
            using (VideoFrame frame = await engine.DecodeImageAsync(source, allocator, new() { Time = new MediaTimestamp(1, new TimeBase(1, 4)) }))
            {
                Assert.Equal(expected, frame.Data.ToArray());
                Assert.False(source.IsDisposed);
                Assert.Equal(expected.Length, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            TgsDocument document = TgsReader.Read(input);
            input.AsSpan().Clear();
            Assert.Equal(expected, TgsRenderer.RenderAtTime(document, .25, options).Pixels.ToArray());
        }

        [Fact]
        public async Task InputDecompressionSelectionAndOutputBoundsArePreserved()
        {
            byte[] input = PartialComposition;
            MediaBufferAllocator allocator = new(4);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new TgsImageDecoder(new() { MaximumInputBytes = 1 }).DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new TgsImageDecoder(new() { MaximumJsonBytes = 1 }).DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await new TgsImageDecoder().DecodeAsync(input, new() { PageIndex = 1 }, allocator));
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await new TgsImageDecoder().DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new TgsImageDecoder().DecodeAsync(input, new(), allocator, new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
