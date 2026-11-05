// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Codecs.Webp;
using Mediana.Core.Memory;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    public class WebpAnimationEncoderTests
    {
        [Fact]
        public async Task RectanglesMixedCompressionFlagsMetadataAndOwnershipArePreserved()
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            using VideoFrame first = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(3, 5, 1), 3, 5, allocator, duration: new(17, new(1, 1000)));
            using VideoFrame second = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(5, 3, 2), 5, 3, allocator, duration: new(29, new(1, 1000)));
            WebpAnimationFrame[] frames =
            [
                new(first) { X = 2, Y = 4, Blending = WebpFrameBlending.AlphaBlend, Disposal = WebpFrameDisposal.Background },
                new(second) { X = 4, Y = 2, Encoding = new() { Compression = WebpCompression.Lossy, Quality = 100 } }
            ];
            WebpAnimationOptions options = new(12, 10)
            {
                LoopCount = 7,
                BackgroundArgb = 0x71533517,
                Encoding = new() { Metadata = new() { IccProfile = new byte[] { 1, 2, 3 }, Exif = new byte[] { 4, 5 }, Xmp = new byte[] { 6, 7, 8 } } }
            };
            long initial = allocator.AllocatedBytes;
            using MemoryStream output = new();
            output.Write(new byte[9]);
            long length = await WebpAnimationEncoder.EncodeAsync(Enumerate(frames), output, allocator, options);
            Assert.Equal(initial, allocator.AllocatedBytes);
            Assert.Equal(9 + length, output.Position);
            byte[] file = output.ToArray()[9..];
            Assert.Equal((uint)(length - 8), BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4)));
            Assert.Equal(62, file[20]);
            Assert.Equal(11, Read24(file.AsSpan(24)));
            Assert.Equal(9, Read24(file.AsSpan(27)));
            Assert.Equal("ICCP", Name(file.AsSpan(30)));
            Assert.Equal("ANIM", Name(file.AsSpan(42)));
            Assert.Equal(0x71533517u, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(50)));
            Assert.Equal(7, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(54)));
            int position = 56;
            for (int index = 0; index < frames.Length; index++)
            {
                WebpAnimationFrame frame = frames[index];
                Assert.Equal("ANMF", Name(file.AsSpan(position)));
                int payload = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 4)));
                Assert.Equal(frame.X / 2, Read24(file.AsSpan(position + 8)));
                Assert.Equal(frame.Y / 2, Read24(file.AsSpan(position + 11)));
                Assert.Equal(frame.Image.Format.Width - 1, Read24(file.AsSpan(position + 14)));
                Assert.Equal(frame.Image.Format.Height - 1, Read24(file.AsSpan(position + 17)));
                Assert.Equal(index == 0 ? 17 : 29, Read24(file.AsSpan(position + 20)));
                Assert.Equal(index == 0 ? 1 : 2, file[position + 23]);
                using MediaBuffer still = WebpEncoder.Encode(frame.Image, allocator, frame.Encoding ?? new());
                int stillStart = index == 0 ? 12 : 30;
                Assert.True(still.Memory.Span[stillStart..].SequenceEqual(file.AsSpan(position + 24, payload - 16)));
                position += 8 + payload;
            }
            Assert.Equal("EXIF", Name(file.AsSpan(position)));
            Assert.Equal("XMP ", Name(file.AsSpan(position + 10)));
            Assert.Equal(file.Length, position + 22);
        }

        [Fact]
        public async Task InvalidRectanglesFlagsAndPerFrameMetadataLeaveDestinationEmpty()
        {
            MediaBufferAllocator allocator = new(16 * 1024 * 1024);
            using VideoFrame image = WebpEncodingTestInput.Frame(WebpEncodingTestInput.Pixels(3, 5, 1), 3, 5, allocator, duration: new(1, new(1, 1000)));
            WebpAnimationFrame[] invalid =
            [
                new(image) { X = 1 }, new(image) { Y = -2 }, new(image) { X = 10 }, new(image) { Y = 8 },
                new(image) { Blending = (WebpFrameBlending)99 }, new(image) { Disposal = (WebpFrameDisposal)99 },
                new(image) { Encoding = new() { Metadata = new() { Xmp = new byte[] { 1 } } } }
            ];
            long initial = allocator.AllocatedBytes;
            foreach (WebpAnimationFrame frame in invalid)
            {
                using MemoryStream output = new();
                await Assert.ThrowsAnyAsync<ArgumentException>(() => WebpAnimationEncoder.EncodeAsync(Enumerate([frame]), output, allocator, new(12, 10)));
                Assert.Empty(output.ToArray());
                Assert.Equal(initial, allocator.AllocatedBytes);
            }
        }

        private static string Name(ReadOnlySpan<byte> data) => System.Text.Encoding.ASCII.GetString(data[..4]);
        private static int Read24(ReadOnlySpan<byte> data) => data[0] | data[1] << 8 | data[2] << 16;
        private static async IAsyncEnumerable<WebpAnimationFrame> Enumerate(IEnumerable<WebpAnimationFrame> frames)
        {
            foreach (WebpAnimationFrame frame in frames) { await Task.Yield(); yield return frame; }
        }
    }
}
