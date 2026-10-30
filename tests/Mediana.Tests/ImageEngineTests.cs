// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.IO;

namespace Mediana.Tests
{
    public class ImageEngineTests
    {
        [Fact]
        public async Task SequentialRecognitionIsReplayedAndOwnedFrameOutlivesInput()
        {
            byte[] bytes = [.. "TEST"u8, .. Enumerable.Range(0, 12000).Select(value => (byte)value)];
            FixtureImageDecoder decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            MediaBufferAllocator allocator = new(64 * 1024);
            ShortReadSource source = new(bytes);
            VideoFrame frame = await engine.DecodeImageAsync(source, allocator);
            Assert.Equal(bytes, decoder.Received);
            Assert.False(source.IsDisposed);
            Assert.Equal(4, allocator.AllocatedBytes);
            await source.DisposeAsync();
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, frame.Data.ToArray());
            frame.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task RandomAccessStartsAtOriginAndPreservesSequentialCursor()
        {
            byte[] bytes = [.. "TEST"u8, 9, 8, 7];
            using MemoryStream memory = new(bytes);
            await using RandomAccessStreamByteSource source = new(memory, leaveOpen: true);
            await source.ReadAsync(new byte[2]);
            FixtureImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(1024);
            using (VideoFrame frame = await new MediaEngineBuilder().AddImageDecoder(decoder).Build().DecodeImageAsync(source, allocator))
            {
                Assert.Equal(bytes, decoder.Received);
                Assert.Equal(2, memory.Position);
                Assert.Equal(4, allocator.AllocatedBytes);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.True(memory.CanRead);
        }

        [Fact]
        public async Task BuildSnapshotsRegistrationsAndRejectsDuplicateOrAmbiguousDecoders()
        {
            MediaEngineBuilder builder = new();
            MediaEngine empty = builder.Build();
            FixtureImageDecoder first = new();
            builder.AddImageDecoder(first);
            Assert.Empty(empty.ImageDecoderImplementations);
            Assert.Equal(new[] { "test.image" }, builder.Build().ImageDecoderImplementations);
            Assert.Throws<ArgumentException>(() => builder.AddImageDecoder(new FixtureImageDecoder()));
            Assert.Throws<ArgumentNullException>(() => builder.AddImageDecoder(null!));
            Assert.Throws<ArgumentException>(() => builder.AddImageDecoder(new FixtureImageDecoder(" ")));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddImageDecoder(new FixtureImageDecoder("zero") { ProbeLength = 0 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddImageDecoder(new FixtureImageDecoder("invalid") { Features = (ImageDecoderFeatures)8 }));
            builder.AddImageDecoder(new FixtureImageDecoder("second"));
            await using ShortReadSource source = new("TESTdata"u8.ToArray());
            MediaBufferAllocator allocator = new(1024);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await builder.Build().DecodeImageAsync(source, allocator));
            Assert.Equal(0, first.DecodeCalls);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task InputByteLimitsReleaseEverythingForKnownAndUnknownLengths(bool known)
        {
            byte[] bytes = [.. "TEST"u8, .. new byte[1021]];
            await using IByteSource source = known ? new TrackingRandomAccessSource(bytes, 7) : new ShortReadSource(bytes);
            FixtureImageDecoder decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            MediaBufferAllocator allocator = new(8192);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.DecodeImageAsync(source, allocator,
                options: new ImageOpenOptions { MaxInputBytes = 1024 }));
            Assert.Equal(0, decoder.DecodeCalls);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ExactInputLimitIsAcceptedAndCapacitySlackIsNotPassedToDecoder()
        {
            byte[] bytes = "TESTdata"u8.ToArray();
            await using ShortReadSource source = new(bytes);
            FixtureImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(1024);
            using (VideoFrame frame = await new MediaEngineBuilder().AddImageDecoder(decoder).Build().DecodeImageAsync(source, allocator,
                options: new ImageOpenOptions { MaxInputBytes = bytes.Length }))
            {
                Assert.Equal(bytes, decoder.Received);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task InputLimitCapsActualProbeWithoutRequiringLargeInputs()
        {
            byte[] bytes = "TESTdata"u8.ToArray();
            FixtureImageDecoder decoder = new() { ProbeLength = 512 };
            MediaBufferAllocator allocator = new(1024);
            await using ShortReadSource source = new(bytes);
            using (VideoFrame frame = await new MediaEngineBuilder().AddImageDecoder(decoder).Build().DecodeImageAsync(source, allocator,
                options: new ImageOpenOptions { MaxInputBytes = bytes.Length }))
            {
                Assert.Equal(bytes, decoder.Received);
            }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task ProbeAndMemoryLimitsApplyBeforeDecodingAndDoNotDisposeSource()
        {
            await using TrackingRandomAccessSource source = new("TESTdata"u8.ToArray());
            FixtureImageDecoder decoder = new();
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            MediaBufferAllocator allocator = new(4);
            await Assert.ThrowsAsync<NotSupportedException>(async () => await engine.DecodeImageAsync(source, allocator,
                options: new ImageOpenOptions { MaxProbeBytes = 3 }));
            Assert.Empty(source.Reads);
            await Assert.ThrowsAsync<MediaMemoryLimitException>(async () => await engine.DecodeImageAsync(source, allocator));
            Assert.False(source.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.Equal(0, decoder.DecodeCalls);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(100)]
        public async Task InvalidReadCountsReleaseProbeMemory(int count)
        {
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new FixtureImageDecoder()).Build();
            await using InvalidRandomAccessSource source = new(count);
            MediaBufferAllocator allocator = new(1024);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await engine.DecodeImageAsync(source, allocator));
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task CancellationBeforeReadAndAfterDecodeReleasesFrameAndInput()
        {
            using CancellationTokenSource cancellation = new();
            FixtureImageDecoder decoder = new(decode: async (encoded, request, allocator, token) =>
            {
                VideoFrame frame = RgbaFrame.CopyFrom([1, 2, 3, 4], 1, 1, VideoColorInfo.Srgb, allocator);
                await cancellation.CancelAsync();
                return frame;
            });
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            await using TrackingRandomAccessSource source = new("TESTdata"u8.ToArray());
            MediaBufferAllocator allocator = new(1024);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await engine.DecodeImageAsync(source, allocator,
                cancellationToken: new CancellationToken(true)));
            Assert.Empty(source.Reads);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await engine.DecodeImageAsync(source, allocator,
                cancellationToken: cancellation.Token));
            Assert.False(source.IsDisposed);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task DecoderFailureIsPropagatedWithoutTryingAnotherFormat()
        {
            FixtureImageDecoder decoder = new(decode: (encoded, request, allocator, token) => throw new InvalidDataException("damaged image"));
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(decoder).Build();
            await using ShortReadSource source = new("TESTdata"u8.ToArray());
            MediaBufferAllocator allocator = new(64 * 1024);
            InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(async () => await engine.DecodeImageAsync(source, allocator));
            Assert.Equal("damaged image", error.Message);
            Assert.Equal(1, decoder.DecodeCalls);
            Assert.Equal(0, allocator.AllocatedBytes);
            Assert.False(source.IsDisposed);
        }

        [Fact]
        public void SelectionParametersMustBeSupportedAndWellFormed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDecodeRequest { PageIndex = -1 }.Validate(ImageDecoderFeatures.Pages));
            Assert.Throws<NotSupportedException>(() => new ImageDecodeRequest { PageIndex = 1 }.Validate(ImageDecoderFeatures.SingleImage));
            Assert.Throws<ArgumentException>(() => new ImageDecodeRequest { Time = default(MediaTimestamp) }.Validate(ImageDecoderFeatures.Animation));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDecodeRequest { Time = new MediaTimestamp(-1, new TimeBase(1, 1)) }.Validate(ImageDecoderFeatures.Animation));
            Assert.Throws<NotSupportedException>(() => new ImageDecodeRequest { Time = new MediaTimestamp(1, new TimeBase(1, 1)) }.Validate(ImageDecoderFeatures.Pages));
            new ImageDecodeRequest { PageIndex = 1 }.Validate(ImageDecoderFeatures.Pages);
            new ImageDecodeRequest { Time = new MediaTimestamp(1, new TimeBase(1, 1)) }.Validate(ImageDecoderFeatures.Animation);
        }
    }
}
