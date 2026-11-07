// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.H264;
using Mediana.Codecs.Png;
using Mediana.Containers.Mp4;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;
using Mediana.Processing;

namespace Mediana.Tests
{
    public class VisualSessionTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task AdditionalImageModuleUsesPublicVisualPathAndRetainsOwnedPixels(bool randomAccess)
        {
            byte[] input = "TESTvisual-content"u8.ToArray();
            FixtureImageDecoder decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).AddContainer(new Mp4ReaderFactory()).Build();
            MediaBufferAllocator allocator = new(64 * 1024);
            await using IByteSource source = randomAccess ? new TrackingRandomAccessSource(input, 1) : new ShortReadSource(input);
            VideoFrame frame;
            await using (VisualSession session = await engine.OpenVisualAsync(source, allocator))
            {
                Assert.True(session.IsImage);
                Assert.Equal(VideoDisplayMatrix.Identity, session.DisplayMatrix);
                frame = await session.ReadFrameAsync() ?? throw new InvalidDataException();
                Assert.Null(await session.ReadFrameAsync());
                Assert.Equal(input, decoder.Received);
            }
            Assert.Equal(4, allocator.AllocatedBytes);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, frame.Data.ToArray());
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task PreviewKeepsRandomCursorAndBorrowsDestinationAndSource()
        {
            using MemoryStream input = new("DSEQvisual"u8.ToArray());
            await using RandomAccessStreamByteSource source = new(input, leaveOpen: true);
            await source.ReadAsync(new byte[3]);
            using MemoryStream output = new();
            List<string> warnings = [];
            MediaBufferAllocator allocator = new(128 * 1024);
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new DelayedImageSequenceDecoder()).Build();
            VisualExportResult result = await engine.ExportPreviewAsync(source, output, new PngImageEncoder(), allocator,
                new() { WarningHandler = warnings.Add });
            Assert.Equal(1, result.Frames); Assert.Equal(1, result.Width); Assert.Equal(1, result.Height);
            Assert.Equal(output.Length, result.BytesWritten); Assert.Equal(3, input.Position);
            Assert.Single(warnings); Assert.True(output.CanWrite); Assert.True(input.CanRead);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RecognizedDecoderFailureDoesNotTryContainerInterpretation()
        {
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new DelayedImageSequenceDecoder { ThrowOnDecode = true })
                .AddContainer(new Mp4ReaderFactory()).Build();
            await using ShortReadSource source = new("DSEQbroken"u8.ToArray());
            using MemoryStream output = new();
            MediaBufferAllocator allocator = new(64 * 1024);
            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => engine.ExportPreviewAsync(source, output,
                new PngImageEncoder(), allocator));
            Assert.Equal("Invalid recognized visual input.", error.Message);
            Assert.Equal(0, output.Length); Assert.False(source.IsDisposed); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task AnimationHonorsTheCancellationTokenOfALaterReadAndClosesItsEnumerator()
        {
            DelayedImageSequenceDecoder decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            await using ShortReadSource source = new("DSEQvisual"u8.ToArray());
            MediaBufferAllocator allocator = new(64 * 1024);
            await using (VisualSession session = await engine.OpenVisualAsync(source, allocator, new() { ReadImageSequence = true }))
            {
                using VideoFrame? first = await session.ReadFrameAsync(); Assert.NotNull(first);
                using CancellationTokenSource cancellation = new();
                Task<VideoFrame?> reading = session.ReadFrameAsync(cancellation.Token).AsTask();
                await decoder.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.ReadFrameAsync().AsTask());
                await cancellation.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reading.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            Assert.True(decoder.Closed); Assert.False(source.IsDisposed); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task VideoUsesSamePublicPreviewPathWithExplicitColorPolicyAndTimestampSelection()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Preview", "untagged-avc.mp4");
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory(new() { TimestampMode = Mp4TimestampMode.LinearMovie }))
                .AddVideoDecoder(new H264DecoderFactory()).Build();
            await using RandomAccessStreamByteSource source = new(File.OpenRead(path));
            MediaBufferAllocator allocator = new(1_000_000);
            using MemoryStream strict = new();
            await Assert.ThrowsAsync<NotSupportedException>(() => engine.ExportPreviewAsync(source, strict, new PngImageEncoder(), allocator));
            Assert.Equal(0, allocator.AllocatedBytes); Assert.Equal(0, strict.Length);
            List<string> warnings = [];
            using MemoryStream output = new();
            VisualExportResult result = await engine.ExportPreviewAsync(source, output, new PngImageEncoder(), allocator, new()
            {
                Input = new() { Request = new() { Time = new MediaTimestamp(0, new TimeBase(1, 1000)) } },
                ColorResolver = SdrPreviewColorPolicy.Resolve, WarningHandler = warnings.Add
            });
            Assert.Equal(1, result.Frames); Assert.NotNull(result.FirstTimestamp); Assert.Single(warnings);
            Assert.Contains("primaries", warnings[0], StringComparison.Ordinal); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ExplicitSelectionUsesItsParserAndPreservesNestedImageRegistration()
        {
            FixtureImageDecoder outer = new("outer", (_, request, allocator, token) =>
                request.Images!.DecodeAsync("PIX"u8.ToArray(), new(), allocator, token));
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(outer).AddImageDecoder(new EmbeddedTestImageDecoder()).Build();
            await using ShortReadSource source = new("NOT-A-RECOGNIZED-HEADER"u8.ToArray());
            MediaBufferAllocator allocator = new(64 * 1024);
            await using (VisualSession session = await engine.OpenVisualAsync(source, allocator,
                new() { Images = new() { DecoderImplementationId = "outer" } }))
            {
                using VideoFrame frame = await session.ReadFrameAsync() ?? throw new InvalidDataException();
                Assert.Equal(new byte[] { 21, 43, 65, 255 }, frame.Data.ToArray());
                Assert.Equal(1, outer.DecodeCalls);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ExplicitDecoderFailureIsNotReplacedByAnotherRecognizableFormat()
        {
            FixtureImageDecoder selected = new("selected", (_, _, _, _) => throw new InvalidDataException("Selected parser failure."));
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(selected).AddImageDecoder(new EmbeddedTestImageDecoder()).Build();
            await using ShortReadSource source = new("PIX"u8.ToArray());
            MediaBufferAllocator allocator = new(64 * 1024);
            await using (VisualSession session = await engine.OpenVisualAsync(source, allocator,
                new() { Images = new() { DecoderImplementationId = "selected" } }))
            {
                InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => session.ReadFrameAsync().AsTask());
                Assert.Equal("Selected parser failure.", error.Message); Assert.Equal(1, selected.DecodeCalls);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MissingExplicitImplementationFailsBeforeReadingAnyInput()
        {
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new EmbeddedTestImageDecoder()).Build();
            await using TrackingRandomAccessSource source = new("PIX"u8.ToArray());
            MediaBufferAllocator allocator = new(64 * 1024);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.OpenVisualAsync(source, allocator,
                new() { Images = new() { DecoderImplementationId = "unknown" } }));
            Assert.Empty(source.Reads); Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task ImageSelectionAndConfiguredInputLimitsReleaseProbeStorage(bool selectStream)
        {
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new DelayedImageSequenceDecoder()).Build();
            await using ShortReadSource source = new("DSEQvisual"u8.ToArray());
            MediaBufferAllocator allocator = new(64 * 1024);
            VisualOpenOptions options = selectStream ? new() { StreamIndex = 0 } : new() { Images = new() { MaxInputBytes = 4 } };
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.OpenVisualAsync(source, allocator, options));
            Assert.Equal(0, allocator.AllocatedBytes); Assert.False(source.IsDisposed);
        }
    }
}
