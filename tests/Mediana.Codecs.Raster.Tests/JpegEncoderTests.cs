// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class JpegEncoderTests
    {
        [Fact]
        public async Task EncodedCorpusMatchesIndependentLibjpegPixels()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Data");
            byte[] source = await File.ReadAllBytesAsync(Path.Combine(root, "qoi-reference.rgba"));
            byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, "jpeg-encode-reference.rgba"));
            MediaBufferAllocator allocator = new(source.Length + 17024);
            using WritableMediaBuffer writable = allocator.Allocate(source.Length);
            source.CopyTo(writable.Memory.Span);
            using MediaBuffer storage = writable.Publish();
            VideoFormat format = new(37, 29, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square);
            using VideoFrame input = new(format, storage, format.CreateTightLayout());
            using MemoryStream output = new();
            await JpegEncoder.EncodeAsync(input, output, allocator, new() { BackgroundColor = new(255, 255, 255) });
            using VideoFrame decoded = await new JpegImageDecoder().DecodeAsync(output.ToArray(), new(), new(64 * 1024));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.InRange(Math.Abs(expected[i] - decoded.Data.Span[i]), 0, i % 4 == 3 ? 0 : 3);
            }
        }

        [Theory]
        [InlineData(VideoChromaSampling.Sampling444)]
        [InlineData(VideoChromaSampling.Sampling422)]
        [InlineData(VideoChromaSampling.Sampling420)]
        [InlineData(VideoChromaSampling.Monochrome)]
        public async Task OddDimensionsAndAllQualityExtremesProduceCompleteBaselineImages(VideoChromaSampling sampling)
        {
            MediaBufferAllocator source = new(19 * 13 * 4);
            using VideoFrame frame = Frame(19, 13, source);
            MediaBufferAllocator workspace = new(17024);
            foreach (int quality in new[] { 0, 1, 50, 75, 100 })
            {
                using JpegTestOutput output = new();
                long written = await new JpegImageEncoder(new() { Quality = quality, ChromaSampling = sampling }).EncodeAsync(frame, output, workspace);
                byte[] encoded = output.Bytes;
                Assert.Equal(encoded.Length, written);
                Assert.Equal(new byte[] { 255, 216 }, encoded[..2]);
                Assert.Equal(new byte[] { 255, 217 }, encoded[^2..]);
                Assert.Equal(0, workspace.AllocatedBytes);
                JpegImageDecoder decoder = new();
                ImageInfo info = await decoder.ReadInfoAsync(encoded);
                Assert.Equal(19, info.Width); Assert.Equal(13, info.Height);
                using VideoFrame decoded = await decoder.DecodeAsync(encoded, new(), new(1_000_000));
                Assert.Equal(new SampleAspectRatio(4, 3), decoded.Format.SampleAspectRatio);
                Assert.IsType<IccColorDescription>(decoded.Format.ColorDescription);
                if (sampling == VideoChromaSampling.Sampling444 && quality == 100)
                {
                    for (int y = 0; y < 13; y++)
                    {
                        for (int x = 0; x < 19 * 4; x++)
                        {
                            Assert.InRange(Math.Abs(frame.GetRow(0, y)[x] - decoded.GetRow(0, y)[x]), 0, 3);
                        }
                    }
                }
            }
        }

        [Fact]
        public async Task AlphaNeedsAnExplicitMatteAndDoesNotLeakHiddenColor()
        {
            MediaBufferAllocator source = new(4);
            using WritableMediaBuffer writable = source.Allocate(4);
            new byte[] { 255, 0, 255, 0 }.CopyTo(writable.Memory.Span);
            using MediaBuffer storage = writable.Publish();
            VideoFormat format = new(1, 1, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square);
            using VideoFrame frame = new(format, storage, format.CreateTightLayout());
            MediaBufferAllocator workspace = new(17024);
            await Assert.ThrowsAsync<NotSupportedException>(() => JpegEncoder.EncodeAsync(frame, Stream.Null, workspace));
            using MemoryStream output = new();
            await JpegEncoder.EncodeAsync(frame, output, workspace, new() { BackgroundColor = new(20, 100, 220), Quality = 100 });
            using VideoFrame decoded = await new JpegImageDecoder().DecodeAsync(output.ToArray(), new(), new(10000));
            Assert.InRange((int)decoded.Data.Span[0], 19, 21);
            Assert.InRange((int)decoded.Data.Span[1], 99, 101);
            Assert.InRange((int)decoded.Data.Span[2], 219, 221);
            Assert.Equal(0, workspace.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationWriteFailuresAndBudgetFailuresReleaseWorkspace()
        {
            MediaBufferAllocator source = new(513 * 97 * 4);
            using VideoFrame frame = Frame(513, 97, source);
            MediaBufferAllocator workspace = new(17024);
            await Assert.ThrowsAsync<OperationCanceledException>(() => JpegEncoder.EncodeAsync(frame, Stream.Null, workspace, cancellationToken: new(true)));
            MediaBufferAllocator tooSmall = new(17023);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(() => JpegEncoder.EncodeAsync(frame, Stream.Null, tooSmall));
            Assert.Equal(0, tooSmall.AllocatedBytes);
            using JpegTestOutput failing = new() { FailWrite = true };
            await Assert.ThrowsAsync<IOException>(() => JpegEncoder.EncodeAsync(frame, failing, workspace));
            Assert.Equal(0, workspace.AllocatedBytes);
            using CancellationTokenSource cancellation = new();
            using JpegTestOutput output = new() { CancelAfterWrite = cancellation };
            await Assert.ThrowsAsync<OperationCanceledException>(() => JpegEncoder.EncodeAsync(frame, output, workspace,
                new() { Quality = 100, ChromaSampling = VideoChromaSampling.Sampling444 }, cancellation.Token));
            Assert.NotEmpty(output.Bytes);
            Assert.Equal(0, workspace.AllocatedBytes);
        }

        [Fact]
        public async Task InputIsRetainedDuringAsynchronousWriting()
        {
            MediaBufferAllocator allocator = new(513 * 97 * 4 + 17024);
            VideoFrame frame = Frame(513, 97, allocator);
            using JpegTestOutput output = new() { ContinueWriting = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            Task<long> pending = JpegEncoder.EncodeAsync(frame, output, allocator, new() { Quality = 100, ChromaSampling = VideoChromaSampling.Sampling444 });
            await output.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            frame.Dispose();
            Assert.Equal(513 * 97 * 4 + 17024, allocator.AllocatedBytes);
            output.ContinueWriting.SetResult();
            await pending;
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(new byte[] { 255, 217 }, output.Bytes[^2..]);
        }

        [Fact]
        public async Task IndependentConcurrentCallsShareNoMutableEncodingState()
        {
            using VideoFrame frame = Frame(101, 77, new(101 * 77 * 4));
            JpegImageEncoder encoder = new();
            MediaBufferAllocator workspace = new(4 * 17024);
            Task<byte[]>[] runs = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                using MemoryStream output = new();
                await encoder.EncodeAsync(frame, output, workspace);
                return output.ToArray();
            })).ToArray();
            byte[][] bytes = await Task.WhenAll(runs);
            foreach (byte[] run in bytes) { Assert.Equal(bytes[0], run); }
            Assert.Equal(0, workspace.AllocatedBytes);
        }

        [Fact]
        public void ForwardTransformMatchesDirectDoublePrecisionDefinition()
        {
            Random random = new(1729);
            float[] samples = new float[64];
            byte[] quantization = new byte[128];
            int[] actual = new int[64];
            float[] reciprocals = new float[64];
            for (int iteration = 0; iteration < 20; iteration++)
            {
                for (int i = 0; i < 64; i++) { samples[i] = random.Next(-128, 128); }
                JpegEncodingTables.Quantization(iteration * 5, quantization);
                for (int i = 0; i < 64; i++) { reciprocals[i] = 1f / quantization[(i & 7) * 8 + i / 8]; }
                JpegForwardTransform.Quantize(samples, reciprocals, actual);
                for (int v = 0; v < 8; v++)
                {
                    for (int u = 0; u < 8; u++)
                    {
                        double sum = 0;
                        for (int y = 0; y < 8; y++)
                        {
                            for (int x = 0; x < 8; x++)
                            {
                                sum += samples[y * 8 + x] * Math.Cos((2 * x + 1) * u * Math.PI / 16)
                                    * Math.Cos((2 * y + 1) * v * Math.PI / 16);
                            }
                        }
                        sum *= (u == 0 ? 1 / Math.Sqrt(2) : 1) * (v == 0 ? 1 / Math.Sqrt(2) : 1) / 4;
                        int expected = (int)Math.Round(sum / quantization[v * 8 + u], MidpointRounding.AwayFromZero);
                        Assert.InRange(Math.Abs(expected - actual[v * 8 + u]), 0, 1);
                    }
                }
            }
        }

        [Fact]
        public async Task InvalidOptionsAreRejectedBeforeWriting()
        {
            using VideoFrame frame = Frame(1, 1, new(4));
            foreach (JpegEncodingOptions options in new JpegEncodingOptions[]
                { new() { Quality = -1 }, new() { Quality = 101 }, new() { ChromaSampling = (VideoChromaSampling)100 } })
            {
                using MemoryStream output = new();
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => JpegEncoder.EncodeAsync(frame, output, new(17024), options));
                Assert.Equal(0, output.Length);
            }
        }

        private static VideoFrame Frame(int width, int height, MediaBufferAllocator allocator)
        {
            using WritableMediaBuffer writable = allocator.Allocate(width * height * 4);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 4;
                    writable.Memory.Span[offset] = (byte)(x * 17 + y * 31);
                    writable.Memory.Span[offset + 1] = (byte)(x * 7 + y * 19);
                    writable.Memory.Span[offset + 2] = (byte)(x * 11 + y * 3);
                    writable.Memory.Span[offset + 3] = 255;
                }
            }
            using MediaBuffer storage = writable.Publish();
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, new(4, 3));
            return new(format, storage, format.CreateTightLayout());
        }
    }
}
