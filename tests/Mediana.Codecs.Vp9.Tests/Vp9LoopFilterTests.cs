// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9LoopFilterTests
    {
        [Fact]
        public async Task FilterWidthsMasksAndRoundingMatchIndependentKernels()
        {
            byte[] fixture = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "filter.bin"));
            using BinaryReader reader = new(new MemoryStream(fixture)); int count = reader.ReadInt32(); Assert.Equal(1152, count);
            for (int record = 0; record < count; record++)
            {
                int depth = reader.ReadInt32(), size = reader.ReadInt32(), level = reader.ReadInt32(), limit = reader.ReadInt32();
                ushort[] input = new ushort[16], expected = new ushort[16];
                for (int i = 0; i < input.Length; i++) { input[i] = reader.ReadUInt16(); }
                for (int i = 0; i < expected.Length; i++) { expected[i] = reader.ReadUInt16(); }
                Verify<ushort>(input, expected, size, level, limit, depth);
                if (depth == 8) { Verify<byte>(input, expected, size, level, limit, depth); }
            }
            Assert.Equal(fixture.Length, reader.BaseStream.Position);
        }

        private static void Verify<T>(ushort[] input, ushort[] expected, int size, int level, int limit, int depth) where T : unmanaged, IBinaryInteger<T>
        {
            T[] samples = new T[16];
            for (int i = 0; i < input.Length; i++) { samples[i] = T.CreateTruncating(input[i]); }
            Vp9LoopFilter.Apply<T>(samples, 8, 1, size, level, limit, depth);
            for (int i = 0; i < expected.Length; i++)
            { Assert.True(expected[i] == int.CreateTruncating(samples[i]), $"Width {4 << size}, depth {depth}, level {level}, limit {limit}, sample {i}: expected {expected[i]}, actual {samples[i]}."); }
            T[] vertical = new T[16 * 3];
            for (int i = 0; i < input.Length; i++) { vertical[i * 3 + 1] = T.CreateTruncating(input[i]); }
            Vp9LoopFilter.Apply<T>(vertical, 8 * 3 + 1, 3, size, level, limit, depth);
            for (int i = 0; i < expected.Length; i++) { Assert.Equal(expected[i], int.CreateTruncating(vertical[i * 3 + 1])); }
        }
    }
}
