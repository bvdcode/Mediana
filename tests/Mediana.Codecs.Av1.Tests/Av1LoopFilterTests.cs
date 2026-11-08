// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Av1.Tests
{
    public class Av1LoopFilterTests
    {
        [Fact]
        public void EveryNativeFilterVectorMatchesAllSamples()
        {
            using FileStream file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Data", "loopfilter.bin.gz"));
            using GZipStream expanded = new(file, CompressionMode.Decompress);
            using BinaryReader reader = new(expanded);
            Assert.Equal("AV1LPF01"u8.ToArray(), reader.ReadBytes(8));
            int count = reader.ReadInt32();
            Assert.Equal(2048, count);
            int changed = 0;
            for (int test = 0; test < count; test++)
            {
                int kind = reader.ReadByte();
                int direction = reader.ReadByte();
                int limit = reader.ReadByte();
                int boundaryLimit = reader.ReadByte();
                int threshold = reader.ReadByte();
                byte[] pixels = reader.ReadBytes(256);
                byte[] expected = reader.ReadBytes(256);
                Assert.Equal(256, pixels.Length);
                Assert.Equal(256, expected.Length);
                if (!pixels.AsSpan().SequenceEqual(expected)) { changed++; }
                int size = kind switch { 4 => 4, 6 or 8 => 8, 14 => 16, _ => throw new InvalidDataException("Invalid fixture filter.") };
                for (int lane = 0; lane < 4; lane++)
                {
                    int origin = 8 * 16 + 8 + (direction == 0 ? lane * 16 : lane);
                    Av1LoopFilterKernel.Apply(pixels, origin, direction == 0 ? 1 : 16, size, kind == 6, limit, boundaryLimit, threshold);
                }
                Assert.True(pixels.AsSpan().SequenceEqual(expected), $"Filter vector {test}, kind {kind}, direction {direction} differs.");
            }
            Assert.True(changed > 200);
            Assert.Equal(-1, expanded.ReadByte());
            Assert.Equal(8, Unsafe.SizeOf<Av1FilterBlock>());
        }
    }
}
