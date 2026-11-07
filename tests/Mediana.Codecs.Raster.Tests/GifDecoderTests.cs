// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text.Json;
using Mediana.Codecs.Gif;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class GifDecoderTests
    {
        [Fact]
        public async Task EveryCompositedFrameMatchesIndependentDecoderIncludingDisposalAndInterlace()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "gif-manifest.json")));
            foreach (IGrouping<string, JsonElement> group in manifest.RootElement.EnumerateArray().GroupBy(entry => entry.GetProperty("file").GetString()!))
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, group.Key));
                GifImageDecoder decoder = new(); MediaBufferAllocator allocator = new(1024 * 1024);
                JsonElement[] expected = group.ToArray(); int index = 0; long start = 0;
                VideoFrame? retained = null; byte[]? opening = null;
                try
                {
                    await foreach (VideoFrame frame in decoder.DecodeFramesAsync(encoded, new(), allocator))
                    {
                        using (frame)
                        {
                            byte[] reference = await File.ReadAllBytesAsync(Path.Combine(root, expected[index].GetProperty("pixels").GetString()!));
                            // Fully transparent RGB is not observable in a composited GIF canvas.
                            Assert.Equal(Visible(reference), Visible(frame.Data.ToArray()));
                            int duration = expected[index].GetProperty("duration").GetInt32();
                            Assert.Equal(duration, frame.Duration!.Value.Value * 10);
                            Assert.Equal(start, frame.Timestamp!.Value.Value * 10);
                            using VideoFrame selected = await decoder.DecodeAsync(encoded, new() { Time = new(start, new(1, 1000)) }, allocator);
                            Assert.Equal(frame.Data.ToArray(), selected.Data.ToArray());
                            if (retained is null) { retained = frame.Retain(); opening = frame.Data.ToArray(); }
                            Assert.Equal(opening, retained.Data.ToArray());
                            start += duration; index++;
                        }
                    }
                    Assert.Equal(expected.Length, index);
                    ImageInfo info = await decoder.ReadInfoAsync(encoded);
                    Assert.Equal(start, info.Duration!.Value.Value * 10);
                }
                finally { retained?.Dispose(); }
                Assert.Equal(0, allocator.AllocatedBytes);
            }
        }
        [Fact]
        public async Task CancellationTruncationBudgetAndEarlyStopReleaseWorkspace()
        {
            byte[] data = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "transparent.gif"));
            GifImageDecoder decoder = new(); MediaBufferAllocator allocator = new(1024 * 1024);
            await foreach (VideoFrame frame in decoder.DecodeFramesAsync(data, new(), allocator)) { frame.Dispose(); break; }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Throws<InvalidDataException>(() => decoder.Decode(data[..^1], new(), allocator));
            Assert.Throws<InvalidDataException>(() => decoder.Decode(data, new() { MaximumPixels = 1 }, allocator));
            Assert.ThrowsAny<OperationCanceledException>(() => decoder.Decode(data, new(), allocator, new(true)));
            MediaBufferAllocator small = new(37 * 29 * 4);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () =>
            {
                await foreach (VideoFrame frame in decoder.DecodeFramesAsync(data, new(), small)) { frame.Dispose(); }
            });
            Assert.Equal(0, small.AllocatedBytes);
            Assert.Equal(0, allocator.AllocatedBytes);
        }
        private static byte[] Visible(byte[] pixels)
        {
            for (int offset = 0; offset < pixels.Length; offset += 4)
            { if (pixels[offset + 3] == 0) { pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 0; } }
            return pixels;
        }
    }
}
