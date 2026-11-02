// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class BorrowedImageDecodeTests
    {
        [Fact]
        public Task ExtractedPngPicturesAndAttachmentsDecodeAfterTheirContainerHasClosed() =>
            PngCoverTestInput.VerifyAsync(Path.Combine(AppContext.BaseDirectory, "Data"));

        [Fact]
        public async Task ExactMemorySliceIsBorrowedAndOnlyIndependentFrameStorageSurvives()
        {
            byte[] backing = [9, .. "TESTimage"u8, 8];
            ReadOnlyMemory<byte> slice = backing.AsMemory(1, 9);
            FixtureImageDecoder decoder = new(decode: (input, _, allocator, _) =>
            {
                Assert.True(input.Equals(slice));
                return ValueTask.FromResult(RgbaFrame.CopyFrom([1, 2, 3, 4], 1, 1, VideoColorInfo.Srgb, allocator));
            });
            MediaBufferAllocator allocator = new(4);
            using (VideoFrame frame = await new MediaEngineBuilder().AddImageDecoder(decoder).Build().DecodeImageAsync(slice, allocator))
            {
                backing.AsSpan().Clear();
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, frame.Data.ToArray());
                Assert.Equal(4, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RegisteredPngUsesOnlyOutputAndTwoRowsForAnAlreadyOwnedInput()
        {
            byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Png", "c6-d16-i1-13x11.png"));
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new PngImageDecoder()).Build();
            MediaBufferAllocator allocator = new(13 * 11 * 4 + 2 * 13 * 8);
            using (VideoFrame frame = await engine.DecodeImageAsync(encoded.AsMemory(), allocator))
            {
                Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "Png", "c6-d16-i1-13x11.rgba")), frame.Data.ToArray());
                Assert.Equal(13 * 11 * 4, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task LimitsAndFeatureSelectionApplyBeforeInvokingDecoder()
        {
            FixtureImageDecoder decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            MediaBufferAllocator allocator = new(4);
            ReadOnlyMemory<byte> encoded = "TESTimage"u8.ToArray();
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.DecodeImageAsync(encoded, allocator,
                options: new() { MaxInputBytes = 8 }));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.DecodeImageAsync(encoded, allocator,
                options: new() { MaxProbeBytes = 3 }));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.DecodeImageAsync(encoded, allocator,
                new() { PageIndex = 1 }));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.DecodeImageAsync(encoded, allocator,
                new() { Time = new MediaTimestamp(1, TimeBase.TimeSpanTicks) }));
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await engine.DecodeImageAsync(encoded, allocator, cancellationToken: cancellation.Token));
            Assert.Equal(0, decoder.DecodeCalls);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task MissingUnknownAndAmbiguousRegistrationsAreExplicit()
        {
            ReadOnlyMemory<byte> encoded = "TESTimage"u8.ToArray();
            MediaBufferAllocator allocator = new(4);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await new MediaEngineBuilder().Build().DecodeImageAsync(encoded, allocator));
            MediaEngineBuilder builder = new MediaEngineBuilder().AddImageDecoder(new FixtureImageDecoder());
            await Assert.ThrowsAsync<NotSupportedException>(async () => await builder.Build().DecodeImageAsync(ReadOnlyMemory<byte>.Empty, allocator));
            builder.AddImageDecoder(new FixtureImageDecoder("other"));
            await Assert.ThrowsAsync<NotSupportedException>(async () => await builder.Build().DecodeImageAsync(encoded, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationAfterDecoderReturnsDisposesItsFrame()
        {
            using CancellationTokenSource cancellation = new();
            FixtureImageDecoder decoder = new(decode: async (_, _, allocator, _) =>
            {
                VideoFrame frame = RgbaFrame.CopyFrom([1, 2, 3, 4], 1, 1, VideoColorInfo.Srgb, allocator);
                await cancellation.CancelAsync();
                return frame;
            });
            MediaBufferAllocator allocator = new(4);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await new MediaEngineBuilder().AddImageDecoder(decoder).Build().DecodeImageAsync("TESTimage"u8.ToArray().AsMemory(), allocator,
                    cancellationToken: cancellation.Token));
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
