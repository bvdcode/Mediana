// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1SequencePictureTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReconstructsIndependentKeyframeSequence(bool omitRepeatedHeaders)
        {
            byte[] encoded = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "sequence.obu"));
            byte[] expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", "sequence.reference.yuv"));
            MediaBufferAllocator allocator = new(1024 * 1024);
            using Av1PictureDecoder decoder = new(allocator);
            int frame = 0;
            foreach (ReadOnlyMemory<byte> packet in AccessUnits(encoded))
            {
                ReadOnlyMemory<byte> input = packet;
                if (omitRepeatedHeaders && frame > 0)
                {
                    Av1ObuReader reader = new(input.Span);
                    while (reader.TryRead(out int type, out _))
                    {
                        if (type == 1)
                        {
                            input = input[reader.ByteOffset..];
                            break;
                        }
                    }
                }
                using Av1Picture picture = decoder.Decode(input.Span);
                int sourceOffset = frame * 48 * 40 * 3 / 2;
                for (int plane = 0; plane < 3; plane++)
                {
                    int shift = picture.Sequence.PlaneShift(plane);
                    int width = 48 >> shift;
                    int height = 40 >> shift;
                    for (int row = 0; row < height; row++)
                    {
                        ReadOnlySpan<byte> actual = picture.Pixels.Memory.Span.Slice(picture.Sequence.PlaneOffset(plane) + row * picture.Sequence.PlaneStride(plane), width);
                        Assert.True(expected.AsSpan(sourceOffset, width).SequenceEqual(actual), $"Frame {frame}, plane {plane}, row {row} differs.");
                        sourceOffset += width;
                    }
                }
                frame++;
            }
            decoder.Dispose();
            Assert.Equal(5, frame);
            Assert.Equal(0, allocator.AllocatedBytes);
        }

        internal static IReadOnlyList<ReadOnlyMemory<byte>> AccessUnits(byte[] encoded)
        {
            List<ReadOnlyMemory<byte>> packets = [];
            Av1ObuReader reader = new(encoded);
            int start = 0;
            int previous = 0;
            while (reader.TryRead(out int type, out _))
            {
                if (type == 2 && previous > start)
                {
                    packets.Add(encoded.AsMemory(start, previous - start));
                    start = previous;
                }
                previous = reader.ByteOffset;
            }
            if (start < encoded.Length)
            {
                packets.Add(encoded.AsMemory(start));
            }
            return packets;
        }
    }
}
