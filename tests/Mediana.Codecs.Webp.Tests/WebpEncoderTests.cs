// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class WebpEncoderTests
    {
        public static IEnumerable<object[]> Samples =>
            from size in new[] { (1, 1), (1, 65), (65, 1), (3, 5), (32, 32), (33, 35), (129, 101), (513, 257) }
            from pattern in Enumerable.Range(0, 4)
            from padded in new[] { false, true }
            select new object[] { size.Item1, size.Item2, pattern, padded };

        [Theory]
        [MemberData(nameof(Samples))]
        public void ExactRgbaGuardsAndBudgetSurviveEncoding(int width, int height, int pattern, bool padded) =>
            WebpEncodingTestInput.Verify(width, height, pattern, padded);

        [Fact]
        public async Task NonSeekableOutputLeavesStreamOpenAndReleasesWorkspace() => await WebpEncodingTestInput.VerifyStreamingAsync();

        [Fact]
        public async Task AnimationPreservesFramePixelsDurationsLoopsAndSingleFrameWorkspace() => await WebpEncodingTestInput.VerifyAnimationAsync();

        [Fact]
        public async Task BudgetsCancellationAndOriginalWriteErrorsReleaseAllWorkspace()
        {
            MediaBufferAllocator source = new(16 * 1024 * 1024);
            using VideoFrame frame = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(65, 33, 2), 65, 33, source);
            MediaBufferAllocator insufficient = new(1);
            Assert.Throws<MediaMemoryLimitException>(() => WebpEncoder.Encode(frame, insufficient));
            Assert.Equal(0, insufficient.AllocatedBytes);
            Assert.Throws<OperationCanceledException>(() => WebpEncoder.Encode(frame, insufficient, new(true)));
            MediaBufferAllocator workspace = new(16 * 1024 * 1024);
            IOException original = new("write failure");
            using ImageOutputTestStream failed = new() { BeforeWrite = (_, _, _) => throw original };
            IOException actual = await Assert.ThrowsAsync<IOException>(() => WebpEncoder.EncodeAsync(frame, failed, workspace));
            Assert.Same(original, actual);
            Assert.Equal(0, workspace.AllocatedBytes);
            using CancellationTokenSource cancellation = new();
            using ImageOutputTestStream cancelled = new() { BeforeWrite = async (_, _, _) => await cancellation.CancelAsync() };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WebpEncoder.EncodeAsync(frame, cancelled, workspace, cancellation.Token));
            Assert.Equal(0, workspace.AllocatedBytes);
        }

        [Fact]
        public async Task AnimationRejectsUnsupportedTimingCanvasAndNonSeekableOutput()
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            using VideoFrame fractional = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(3, 5, 1), 3, 5, allocator,
                duration: new MediaDuration(1, new(1, 24)));
            using MemoryStream output = new();
            await Assert.ThrowsAsync<NotSupportedException>(() => WebpAnimationEncoder.EncodeAsync(WebpEncodingTestInput.Enumerate([fractional]), output, allocator));
            Assert.Empty(output.ToArray());
            using ImageOutputTestStream sequential = new();
            await Assert.ThrowsAsync<ArgumentException>(() => WebpAnimationEncoder.EncodeAsync(WebpEncodingTestInput.Enumerate([fractional]), sequential, allocator));
            Assert.Empty(sequential.ToArray());
            using VideoFrame first = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(3, 5, 1), 3, 5, allocator, duration: new(100, new(1, 1000)));
            using VideoFrame second = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(5, 3, 2), 5, 3, allocator, duration: new(100, new(1, 1000)));
            long initial = allocator.AllocatedBytes;
            await Assert.ThrowsAsync<ArgumentException>(() => WebpAnimationEncoder.EncodeAsync(WebpEncodingTestInput.Enumerate([first, second]), output, allocator));
            Assert.Equal(initial, allocator.AllocatedBytes);
        }

        [Fact]
        public void EncodingRequiresExplicitPixelColorAndAspectInterpretation()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame input = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(3, 5, 1), 3, 5, allocator);
            using WritableMediaBuffer writable = allocator.Allocate(input.Data.Length);
            input.Data.Span.CopyTo(writable.Memory.Span);
            using MediaBuffer owned = writable.Publish();
            foreach (VideoFormat format in new[]
            {
                new VideoFormat(3, 5, VideoPixelFormat.Rgba32),
                new VideoFormat(3, 5, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, new(2, 1))
            })
            {
                using VideoFrame frame = new(format, owned, format.CreateTightLayout());
                Assert.Throws<NotSupportedException>(() => WebpEncoder.Encode(frame, allocator));
            }
        }

        [Fact]
        public async Task PendingOutputRetainsInputAndConcurrentCallsOwnIndependentWorkspace()
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            byte[] pixels = WebpEncodingTestInput.Pixels(33, 35, 2);
            using VideoFrame frame = WebpEncodingTestInput.Frame(pixels, 33, 35, allocator);
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using ImageOutputTestStream delayed = new()
            {
                BeforeWrite = async (_, _, token) =>
                {
                    entered.SetResult();
                    await resume.Task.WaitAsync(token);
                }
            };
            WebpImageEncoder encoder = new();
            Task<long> first = encoder.EncodeAsync(frame, delayed, allocator);
            await entered.Task;
            using ImageOutputTestStream concurrent = new();
            await encoder.EncodeAsync(frame, concurrent, allocator);
            frame.Dispose();
            Assert.True(allocator.AllocatedBytes > 0);
            resume.SetResult();
            await first;
            Assert.Equal(delayed.ToArray(), concurrent.ToArray());
            Assert.Equal(pixels, WebpLosslessTestReader.Decode(delayed.ToArray(), out _, out _));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(16384, 1)]
        [InlineData(1, 16384)]
        public void Vp8lDimensionFieldAcceptsItsExactMaximum(int width, int height) =>
            WebpEncodingTestInput.Verify(width, height, 0, true);

        [Fact]
        public void DimensionsOutsideTheBitstreamRangeAreRejectedBeforeAllocatingWorkspace()
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame frame = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(16385, 1, 0), 16385, 1, allocator);
            MediaBufferAllocator workspace = new(1);
            Assert.Throws<NotSupportedException>(() => WebpEncoder.Encode(frame, workspace));
            Assert.Equal(0, workspace.AllocatedBytes);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void CanonicalCodesRoundTripSparseSingletonAndDepthLimitedAlphabets(int kind)
        {
            int[] frequencies = new int[280];
            switch (kind)
            {
                case 0: frequencies[279] = 1; break;
                case 1: frequencies[0] = 1; frequencies[255] = 1; break;
                case 2: Array.Fill(frequencies, 1); break;
                case 3:
                    frequencies[0] = 1;
                    frequencies[1] = 1;
                    for (int index = 2; index < 30; index++) { frequencies[index] = frequencies[index - 1] + frequencies[index - 2]; }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            WebpHuffmanCode tree = new(frequencies);
            byte[] encoded = new byte[4096];
            WebpBitWriter writer = new(encoded);
            tree.WriteHeader(writer);
            int[] symbols = Enumerable.Range(0, frequencies.Length).Where(symbol => frequencies[symbol] != 0).ToArray();
            foreach (int symbol in symbols) { tree.WriteSymbol(writer, symbol); }
            writer.Complete();
            WebpTestBitReader reader = new(encoded);
            WebpTestHuffman decoder = WebpTestHuffman.Read(reader, frequencies.Length);
            foreach (int symbol in symbols) { Assert.Equal(symbol, decoder.Decode(reader)); }
        }
    }
}
