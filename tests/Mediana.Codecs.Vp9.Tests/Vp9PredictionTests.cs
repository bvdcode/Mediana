// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9PredictionTests
    {
        [Fact]
        public async Task AllIntraModesMatchIndependentBoundaryAndPrecisionVectors()
        {
            byte[] fixture = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "prediction.bin"));
            using BinaryReader reader = new(new MemoryStream(fixture)); int count = reader.ReadInt32(); Assert.Equal(600, count);
            for (int record = 0; record < count; record++)
            {
                int logSize = reader.ReadInt32(), mode = reader.ReadInt32(), depth = reader.ReadInt32(), flags = reader.ReadInt32();
                int size = 1 << logSize; ushort[] expected = new ushort[size * size];
                for (int i = 0; i < expected.Length; i++) { expected[i] = reader.ReadUInt16(); }
                Verify<ushort>(expected, size, mode, depth, flags);
                if (depth == 8) { Verify<byte>(expected, size, mode, depth, flags); }
            }
            Assert.Equal(fixture.Length, reader.BaseStream.Position);
        }

        private static void Verify<T>(ushort[] expected, int size, int mode, int depth, int flags) where T : unmanaged, IBinaryInteger<T>
        {
            T[] samples = new T[128 * 128]; int max = (1 << depth) - 1;
            for (int i = 0; i < samples.Length; i++) { samples[i] = T.CreateTruncating((i * 97 + (i >> 7) * 53 + 17) & max); }
            Vp9IntraPrediction.Predict<T>(samples, 128, 127, 127, 40, 40, size, mode, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0, depth);
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    int actual = int.CreateTruncating(samples[(40 + row) * 128 + 40 + column]);
                    Assert.True(expected[row * size + column] == actual, $"Size {size}, mode {mode}, depth {depth}, boundaries {flags}, ({column},{row}): expected {expected[row * size + column]}, actual {actual}.");
                }
            }
        }
    }
}
