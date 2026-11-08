// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Runtime.CompilerServices;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Formats;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1CdefTests
    {
        [Fact]
        public void NativeDirectionsAndFilterVectorsMatchEverySample()
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "cdef.bin.gz"));
            using GZipStream expanded = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(expanded);
            Assert.Equal("AV1CDEF1"u8.ToArray(), reader.ReadBytes(8));
            Assert.Equal(1024, reader.ReadInt32());
            int changed = 0;
            for (int test = 0; test < 1024; test++)
            {
                int side = reader.ReadByte();
                int x = reader.ReadByte();
                int y = reader.ReadByte();
                int primary = reader.ReadByte();
                int secondary = reader.ReadByte();
                int damping = reader.ReadByte();
                int direction = reader.ReadByte();
                int expectedDirection = reader.ReadByte();
                int expectedVariance = reader.ReadInt32();
                byte[] pixels = reader.ReadBytes(256);
                byte[] expected = reader.ReadBytes(256);
                Assert.Equal(256, pixels.Length);
                Assert.Equal(256, expected.Length);
                Assert.Equal(expectedDirection, Av1CdefDirection.Find(pixels.AsSpan(4 * 16 + 4), 16, out int variance));
                Assert.Equal(expectedVariance, variance);
                byte[] scratch = new byte[(side + 4) * 16];
                for (int row = 0; row < side + 4; row++)
                {
                    int source = y + row - 2;
                    if ((uint)source < 16) { pixels.AsSpan(source * 16, 16).CopyTo(scratch.AsSpan(row * 16)); }
                }
                if (!pixels.AsSpan().SequenceEqual(expected)) { changed++; }
                Av1CdefKernel.Filter(scratch, pixels, 16, 16, x, y, side, primary, secondary, damping, direction);
                Assert.True(pixels.AsSpan().SequenceEqual(expected), $"CDEF vector {test} differs.");
            }
            Assert.True(changed > 500);
            Assert.Equal(-1, expanded.ReadByte());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void ScopedBudgetFailureReleasesEveryFilterWorkspace(int allocation)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "cdefMono.obu"));
            int samples = 120 * 88;
            int metadata = 30 * 22 * Unsafe.SizeOf<Av1FilterBlock>();
            int indices = 4;
            int contexts = (30 + Av1TileDecoder.LeftContexts) * Unsafe.SizeOf<Av1BlockContext>();
            int scratch = 120 * 12;
            int reserved = allocation switch
            {
                0 => samples,
                1 => samples + metadata,
                2 => samples + metadata + indices + contexts + samples / 16,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            };
            int requested = allocation switch
            {
                0 => metadata,
                1 => indices,
                2 => scratch,
                _ => throw new ArgumentOutOfRangeException(nameof(allocation))
            };
            MediaBufferAllocator input = new(1024 * 1024);
            MediaBufferAllocator parent = new(1024 * 1024);
            MediaBufferAllocator allocator = parent.CreateScope(reserved + requested - 1);
            EncodedStreamInfo stream = new(0, MediaKind.Video, "av1", StreamFormats.Av1, new(1, 25), new byte[] { 0x81, 0, 0x1C, 0 });
            using (IMediaCodec<MediaPacket, VideoFrame> codec = new Av1DecoderFactory().Create(stream, allocator))
            {
                using WritableMediaBuffer write = input.Allocate(encoded.Length);
                encoded.CopyTo(write.Span);
                using MediaBuffer buffer = write.Publish();
                using MediaPacket packet = new(0, buffer, new(0, new(1, 25)));
                MediaMemoryLimitException error = Assert.Throws<MediaMemoryLimitException>(() => codec.TrySend(packet));
                Assert.Equal(requested, error.RequestedBytes);
                Assert.Equal(0, parent.AllocatedBytes);
                Assert.Equal(0, allocator.AllocatedBytes);
                Assert.Throws<InvalidOperationException>(() => codec.Receive());
                codec.Reset();
                byte[] small = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "flat.obu"));
                using WritableMediaBuffer smallWrite = input.Allocate(small.Length);
                small.CopyTo(smallWrite.Span);
                using MediaBuffer smallBuffer = smallWrite.Publish();
                using MediaPacket smallPacket = new(0, smallBuffer, new(0, new(1, 25)));
                Assert.Equal(CodecInputStatus.Accepted, codec.TrySend(smallPacket));
                codec.Receive().Value.Dispose();
            }
            Assert.Equal(0, input.AllocatedBytes);
            Assert.Equal(0, parent.AllocatedBytes);
        }

        [Theory]
        [InlineData("cdefRegions", 160, 136, false, 3)]
        [InlineData("cdefMono", 117, 83, true, 1)]
        [InlineData("cdefMonoZero", 117, 83, true, 1)]
        [InlineData("cdefMonoRaw", 117, 83, true, 1)]
        [InlineData("cdefOdd", 37, 29, false, 1)]
        public void ReconstructsIndependentFilteredRegionsAndPartialEdges(string name, int width, int height, bool monochrome, int count)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".obu"));
            byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", name + ".yuv"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using Av1PictureDecoder decoder = new(allocator);
            int frame = 0;
            int sourceOffset = 0;
            bool indexed = false;
            foreach (ReadOnlyMemory<byte> packet in Av1SequencePictureTests.AccessUnits(encoded))
            {
                using Av1Picture picture = decoder.Decode(packet.Span);
                Assert.Equal(name is not ("cdefMonoZero" or "cdefMonoRaw"), picture.Header.Cdef.Enabled);
                Assert.Equal(monochrome, picture.Sequence.Monochrome);
                indexed |= picture.Header.Cdef.IndexBits > 0;
                Assert.Equal(picture.Sequence.Stride * (monochrome ? 12 : 20), Av1Cdef.ScratchLength(picture.Sequence));
                for (int plane = 0; plane < (monochrome ? 1 : 3); plane++)
                {
                    int shift = picture.Sequence.PlaneShift(plane);
                    int planeWidth = (width + (1 << shift) - 1) >> shift;
                    int planeHeight = (height + (1 << shift) - 1) >> shift;
                    int offset = picture.Sequence.PlaneOffset(plane);
                    int stride = picture.Sequence.PlaneStride(plane);
                    for (int row = 0; row < planeHeight; row++)
                    {
                        Assert.True(expected.AsSpan(sourceOffset, planeWidth).SequenceEqual(picture.Pixels.Memory.Span.Slice(offset + row * stride, planeWidth)), $"Frame {frame}, plane {plane}, row {row} differs.");
                        sourceOffset += planeWidth;
                    }
                }
                frame++;
            }
            Assert.Equal(count, frame);
            Assert.Equal(expected.Length, sourceOffset);
            if (name == "cdefRegions") { Assert.True(indexed); }
            decoder.Dispose();
            Assert.Equal(0, allocator.AllocatedBytes);
        }
    }
}
