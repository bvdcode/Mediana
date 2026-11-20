// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;
using Mediana.Codecs.Bmp;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Codecs.Raster.Tests
{
    public class AniDecoderTests
    {
        [Fact]
        public async Task SelectedStepsAndExactTimesUseSharedIconDecoding()
        {
            byte[] encoded = Animation([1, 0, 1], [2, 3, 4]);
            MediaBufferAllocator allocator = new(64);
            AniImageDecoder decoder = new();
            using (VideoFrame opening = await decoder.DecodeAsync(encoded, new(), allocator))
            {
                Assert.Equal(new byte[] { 0, 255, 0, 128 }, opening.Data.ToArray());
                Assert.Equal(new MediaTimestamp(0, new(1, 60)), opening.Timestamp);
                Assert.Equal(new MediaDuration(2, new(1, 60)), opening.Duration);
            }
            using (VideoFrame atBoundary = await decoder.DecodeAsync(encoded, new() { Time = new(2, new(1, 60)) }, allocator))
            {
                Assert.Equal(new byte[] { 255, 0, 0, 255 }, atBoundary.Data.ToArray());
                Assert.Equal(new MediaTimestamp(2, new(1, 60)), atBoundary.Timestamp);
            }
            using (VideoFrame step = await decoder.DecodeAsync(encoded, new() { PageIndex = 2 }, allocator))
            {
                Assert.Equal(new MediaTimestamp(5, new(1, 60)), step.Timestamp);
            }
            ImageInfo info = await decoder.ReadInfoAsync(encoded);
            Assert.Equal("ani", info.FormatId); Assert.Equal(1, info.Width); Assert.Equal(3, info.PageCount);
            Assert.Equal(new MediaDuration(9, new(1, 60)), info.Duration);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task EnumerationRetainsIndependentFramesAndEarlyDisposalReleasesWorkspace()
        {
            byte[] encoded = Animation([1, 0, 1], [2, 3, 4]);
            AniImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(64);
            List<VideoFrame> frames = [];
            try
            {
                await foreach (VideoFrame frame in decoder.DecodeFramesAsync(encoded, new(), allocator)) { frames.Add(frame); }
                Assert.Equal(3, frames.Count); Assert.Equal(12, allocator.AllocatedBytes);
                Assert.Equal(new byte[] { 0, 255, 0, 128 }, frames[0].Data.ToArray());
                Assert.Equal(new byte[] { 255, 0, 0, 255 }, frames[1].Data.ToArray());
            }
            finally { foreach (VideoFrame frame in frames) { frame.Dispose(); } }
            Assert.Equal(0, allocator.AllocatedBytes);
            await foreach (VideoFrame frame in decoder.DecodeFramesAsync(encoded, new(), allocator)) { frame.Dispose(); break; }
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        [Fact]
        public async Task FractionalJiffySkipsZeroDurationOpeningStep()
        {
            byte[] encoded = Animation([1, 0, 1], [0, 3, 4]);
            MediaBufferAllocator allocator = new(64);
            using VideoFrame frame = await new AniImageDecoder().DecodeAsync(encoded, new() { Time = new(1, new(1, 600)) }, allocator);
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, frame.Data.ToArray());
        }

        [Fact]
        public void MalformedChunksAndSelectionsFailWithoutAllocatingAllFrames()
        {
            byte[] valid = Animation([1, 0, 1], [2, 3, 4]);
            AniImageDecoder decoder = new();
            MediaBufferAllocator allocator = new(4);
            ImageDecodeRequest explicitRequest = new() { Images = new([new IcoImageDecoder()]) };
            for (int length = 0; length < valid.Length; length++)
            {
                Assert.Throws<InvalidDataException>(() => decoder.Decode(valid[..length], explicitRequest, allocator));
                Assert.Equal(0, allocator.AllocatedBytes);
            }
            Assert.Throws<InvalidDataException>(() => decoder.Decode(Animation([3, 0, 1], [2, 3, 4]), new(), allocator));
            Assert.Throws<InvalidDataException>(() => decoder.Decode(Animation([1, 0, 1], [2, 3]), new(), allocator));
            Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Decode(valid, new() { PageIndex = 3 }, allocator));
            Assert.Throws<ArgumentOutOfRangeException>(() => decoder.Decode(valid, new() { Time = new(9, new(1, 60)) }, allocator));
            Assert.Throws<ArgumentException>(() => decoder.Decode(valid, new() { PageIndex = 1, Time = new(1, new(1, 600)) }, allocator));
            Assert.Throws<OperationCanceledException>(() => decoder.Decode(valid, new(), allocator, new(true)));
            using VideoFrame output = decoder.Decode(valid, new() { PageIndex = 2 }, allocator);
            Assert.Equal(4, allocator.AllocatedBytes);
        }

        private static byte[] Animation(uint[] sequence, uint[] rates)
        {
            byte[] header = Words([36, 2, 3, 1, 1, 32, 1, 6, 3]);
            byte[] frames = Chunk("LIST", [.. "fram"u8, .. Chunk("icon", Icon(255, 0, 255)), .. Chunk("icon", Icon(0, 255, 128))]);
            byte[] body = [.. "ACON"u8, .. Chunk("JUNK", [7]), .. Chunk("anih", header), .. Chunk("seq ", Words(sequence)), .. Chunk("rate", Words(rates)), .. frames];
            return Chunk("RIFF", body);
        }
        private static byte[] Words(uint[] values)
        {
            byte[] bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * 4), values[i]); }
            return bytes;
        }
        private static byte[] Chunk(string type, byte[] payload)
        {
            byte[] chunk = new byte[8 + payload.Length + (payload.Length & 1)];
            Encoding.ASCII.GetBytes(type).CopyTo(chunk, 0);
            BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(4), payload.Length);
            payload.CopyTo(chunk, 8);
            if ((payload.Length & 1) != 0) { chunk[^1] = 42; }
            return chunk;
        }
        private static byte[] Icon(byte red, byte green, byte alpha)
        {
            byte[] bytes = new byte[70];
            bytes[2] = 1; bytes[4] = 1; bytes[6] = 1; bytes[7] = 1; bytes[10] = 1; bytes[12] = 32; bytes[14] = 48; bytes[18] = 22;
            bytes[22] = 40; bytes[26] = 1; bytes[30] = 2; bytes[34] = 1; bytes[36] = 32;
            bytes[63] = green; bytes[64] = red; bytes[65] = alpha;
            return bytes;
        }
    }
}
