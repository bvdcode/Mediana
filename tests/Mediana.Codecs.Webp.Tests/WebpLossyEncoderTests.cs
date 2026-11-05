// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class WebpLossyEncoderTests
    {
        public static IEnumerable<object[]> Samples =>
            from size in new[] { (1, 1), (1, 65), (65, 1), (3, 5), (16, 16), (33, 35), (129, 101) }
            from quality in new[] { 0, 75, 100 }
            from pattern in Enumerable.Range(0, 4)
            select new object[] { size.Item1, size.Item2, quality, pattern };

        [Theory]
        [MemberData(nameof(Samples))]
        public void PartitionsAlphaAndOwnershipSurvivePaddedInput(int width, int height, int quality, int pattern)
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            byte[] rgba = WebpEncodingTestInput.Pixels(width, height, pattern);
            using VideoFrame frame = WebpEncodingTestInput.Frame(rgba, width, height, allocator, true);
            byte[] untouched = frame.Data.ToArray();
            long initial = allocator.AllocatedBytes;
            WebpEncodingOptions options = new() { Compression = WebpCompression.Lossy, Quality = quality };
            using (MediaBuffer encoded = WebpEncoder.Encode(frame, allocator, options))
            {
                ReadOnlySpan<byte> file = encoded.Memory.Span;
                Assert.True(file[..4].SequenceEqual("RIFF"u8));
                Assert.Equal(file.Length - 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(file[4..]));
                Assert.True(file.Slice(8, 4).SequenceEqual("WEBP"u8));
                int position = 12;
                while (position < file.Length)
                {
                    ReadOnlySpan<byte> name = file.Slice(position, 4);
                    int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 4)..]));
                    ReadOnlySpan<byte> payload = file.Slice(position + 8, length);
                    if (name.SequenceEqual("VP8X"u8))
                    {
                        Assert.Equal(10, length);
                        Assert.Equal(16, payload[0]);
                    }
                    else if (name.SequenceEqual("ALPH"u8))
                    {
                        Assert.Equal(0, payload[0]);
                        Assert.Equal(width * height + 1, payload.Length);
                        for (int pixel = 0; pixel < width * height; pixel++) { Assert.Equal(rgba[pixel * 4 + 3], payload[pixel + 1]); }
                    }
                    else
                    {
                        Assert.True(name.SequenceEqual("VP8 "u8));
                        VerifyHeader(payload, width, height, quality);
                    }
                    if ((length & 1) != 0) { Assert.Equal(0, file[position + 8 + length]); }
                    position += 8 + length + (length & 1);
                }
                Assert.Equal(file.Length, position);
                Assert.Equal(initial + file.Length, allocator.AllocatedBytes);
                Assert.Equal(untouched, frame.Data.ToArray());
            }
            Assert.Equal(initial, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FailedEncodingReleasesAllChargedWorkspace()
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            using VideoFrame frame = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(129, 101, 2), 129, 101, allocator);
            WebpEncodingOptions options = new() { Compression = WebpCompression.Lossy };
            MediaBufferAllocator insufficient = new(100);
            Assert.Throws<MediaMemoryLimitException>(() => WebpEncoder.Encode(frame, insufficient, options));
            Assert.Equal(0, insufficient.AllocatedBytes);
            Assert.Throws<OperationCanceledException>(() => WebpEncoder.Encode(frame, allocator, options, new(true)));
            long initial = allocator.AllocatedBytes;
            IOException original = new("write failure");
            using ImageOutputTestStream output = new() { BeforeWrite = (_, _, _) => throw original };
            Assert.Same(original, await Assert.ThrowsAsync<IOException>(() => WebpEncoder.EncodeAsync(frame, output, allocator, options)));
            Assert.Equal(initial, allocator.AllocatedBytes);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(101)]
        public void InvalidQualityIsRejectedBeforeAllocating(int quality)
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            using VideoFrame frame = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(1, 1, 0), 1, 1, allocator);
            long initial = allocator.AllocatedBytes;
            Assert.Throws<ArgumentOutOfRangeException>(() => WebpEncoder.Encode(frame, allocator,
                new WebpEncodingOptions { Compression = WebpCompression.Lossy, Quality = quality }));
            Assert.Throws<ArgumentOutOfRangeException>(() => WebpEncoder.Encode(frame, allocator,
                new WebpEncodingOptions { Compression = (WebpCompression)99 }));
            Assert.Equal(initial, allocator.AllocatedBytes);
        }

        private static void VerifyHeader(ReadOnlySpan<byte> payload, int width, int height, int quality)
        {
            int tag = payload[0] | payload[1] << 8 | payload[2] << 16;
            Assert.Equal(16, tag & 31);
            Assert.True(payload.Slice(3, 3).SequenceEqual(new byte[] { 0x9d, 0x01, 0x2a }));
            Assert.Equal(width, BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]));
            Assert.Equal(height, BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]));
            int partitionLength = tag >> 5;
            Assert.InRange(partitionLength, 4, payload.Length - 14);
            Vp8TestBooleanReader modes = new(payload.Slice(10, partitionLength).ToArray());
            Assert.Equal(0, modes.Literal(4));
            Assert.Equal(0, modes.Literal(6));
            Assert.Equal(0, modes.Literal(3));
            Assert.False(modes.Read());
            Assert.Equal(0, modes.Literal(2));
            Assert.Equal((127 * (100 - quality) + 50) / 100, modes.Literal(7));
        }
    }
}
