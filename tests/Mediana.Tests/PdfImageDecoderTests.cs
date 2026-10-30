// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Drawing;
using Mediana.IO;

namespace Mediana.Tests
{
    public class PdfImageDecoderTests
    {
        private static string Input => Path.Combine(AppContext.BaseDirectory, "Data", "Pdf", "graphics.pdf");

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public async Task OrdinalPageSelectionMatchesRenderingAndFrameOutlivesAllInput(int index)
        {
            byte[] input = await File.ReadAllBytesAsync(Input);
            PdfRenderOptions options = new() { Dpi = 72, Supersampling = 1 };
            byte[] expected;
            using (PdfDocument document = await PdfReader.ReadFileAsync(Input))
            {
                expected = PdfRenderer.Render(document.GetPage(index + 1), options).Pixels.ToArray();
            }
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new PdfImageDecoder(renderOptions: options)).Build();
            MediaBufferAllocator allocator = new(1024 * 1024);
            ShortReadSource source = new(input);
            using (VideoFrame frame = await engine.DecodeImageAsync(source, allocator, new() { PageIndex = index }))
            {
                Assert.False(source.IsDisposed);
                await source.DisposeAsync();
                input.AsSpan().Clear();
                Assert.Equal(expected, frame.Data.ToArray());
                Assert.Equal(SampleAspectRatio.Square, frame.Format.SampleAspectRatio);
                Assert.Equal(VideoColorInfo.Srgb, frame.Format.Color);
                Assert.Null(frame.Timestamp);
                Assert.Null(frame.Duration);
                Assert.Equal(expected.Length, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ResolutionAndInputPageOperationSampleAndMemoryLimitsRemainEffective()
        {
            byte[] input = await File.ReadAllBytesAsync(Input);
            MediaBufferAllocator allocator = new(1024 * 1024);
            using (VideoFrame reduced = await new PdfImageDecoder(renderOptions: new() { Dpi = 72, MaximumWidth = 20 }).DecodeAsync(input, new(), allocator))
            {
                Assert.Equal(20, reduced.Format.Width);
                Assert.Equal(20, reduced.Format.Height);
            }
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await new PdfImageDecoder().DecodeAsync(input, new() { PageIndex = int.MaxValue }, allocator));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await new PdfImageDecoder().DecodeAsync(input,
                new() { Time = new MediaTimestamp(1, new TimeBase(1, 1)) }, allocator));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new PdfImageDecoder(new() { MaximumInputBytes = 1 }).DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new PdfImageDecoder(new() { MaximumPages = 1 }).DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await new PdfImageDecoder(new() { MaximumOperations = 1 }).DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await new PdfImageDecoder(renderOptions: new() { MaximumSamples = 1 }).DecodeAsync(input, new(), allocator));
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await new PdfImageDecoder().DecodeAsync(input, new(), new MediaBufferAllocator(1)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new PdfImageDecoder().DecodeAsync(input, new(), allocator, new CancellationToken(true)));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
