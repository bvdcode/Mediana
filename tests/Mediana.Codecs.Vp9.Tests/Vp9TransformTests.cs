// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Vp9;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9TransformTests
    {
        [Fact]
        public async Task OneDimensionalTransformsMatchIndependentIntegerKernels()
        {
            byte[] fixture = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "transform-1d.bin"));
            using BinaryReader reader = new(new MemoryStream(fixture)); int count = reader.ReadInt32();
            Assert.Equal(448, count);
            for (int record = 0; record < count; record++)
            {
                int logSize = reader.ReadInt32(); bool adst = reader.ReadInt32() != 0; int[] actual = new int[1 << logSize];
                for (int i = 0; i < actual.Length; i++) { actual[i] = reader.ReadInt32(); }
                Vp9Transform.OneDimension(actual, logSize, adst);
                for (int i = 0; i < actual.Length; i++)
                {
                    int expected = reader.ReadInt32();
                    Assert.True(expected == actual[i], $"Record {record}, size {actual.Length}, ADST {adst}, position {i}: expected {expected}, actual {actual[i]}.");
                }
            }
            Assert.Equal(fixture.Length, reader.BaseStream.Position);
        }

        [Fact]
        public void RepeatedTransformsDoNotAllocateManagedStorage()
        {
            int[] block = new int[1024];
            for (int i = 0; i < 100; i++) { block.AsSpan().Clear(); block[0] = 128; Vp9Transform.Inverse(block, 5, Vp9TransformType.DctDct, false); }
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { block.AsSpan().Clear(); block[0] = 128; Vp9Transform.Inverse(block, 5, Vp9TransformType.DctDct, false); }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        }

        [Fact]
        public async Task TwoDimensionalReconstructionMatchesAllSizesTypesAndPrecisions()
        {
            byte[] fixture = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "transform-2d.bin"));
            using BinaryReader reader = new(new MemoryStream(fixture)); int count = reader.ReadInt32();
            Assert.Equal(336, count);
            for (int record = 0; record < count; record++)
            {
                int logSize = reader.ReadInt32(), type = reader.ReadInt32(), depth = reader.ReadInt32(); int[] actual = new int[1 << (2 * logSize)];
                for (int i = 0; i < actual.Length; i++) { actual[i] = reader.ReadInt32(); }
                Vp9Transform.Inverse(actual, logSize, type == 4 ? Vp9TransformType.DctDct : (Vp9TransformType)type, type == 4);
                for (int i = 0; i < actual.Length; i++)
                {
                    int expected = reader.ReadUInt16(), pixel = Math.Clamp((1 << (depth - 1)) + actual[i], 0, (1 << depth) - 1);
                    Assert.True(expected == pixel, $"Record {record}, log size {logSize}, type {type}, depth {depth}, position {i}: expected {expected}, actual {pixel}.");
                }
            }
            Assert.Equal(fixture.Length, reader.BaseStream.Position);
        }
    }
}
