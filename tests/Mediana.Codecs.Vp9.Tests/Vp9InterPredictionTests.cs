// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9InterPredictionTests
    {
        [Fact]
        public async Task FractionalAndScaledPredictionsMatchIndependentConvolution()
        {
            byte[] fixture = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "interpolation.bin"));
            using BinaryReader reader = new(new MemoryStream(fixture)); int count = reader.ReadInt32(); Assert.Equal(768, count);
            for (int record = 0; record < count; record++)
            {
                int depth = reader.ReadInt32(), filter = reader.ReadInt32(), width = reader.ReadInt32(), height = reader.ReadInt32();
                int startX = reader.ReadInt32(), startY = reader.ReadInt32(), stepX = reader.ReadInt32(), stepY = reader.ReadInt32();
                ushort[] expected = new ushort[width * height];
                for (int i = 0; i < expected.Length; i++) { expected[i] = reader.ReadUInt16(); }
                Verify<ushort>(expected, depth, filter, width, height, startX, startY, stepX, stepY);
                if (depth == 8) { Verify<byte>(expected, depth, filter, width, height, startX, startY, stepX, stepY); }
            }
            Assert.Equal(fixture.Length, reader.BaseStream.Position);
        }

        private static void Verify<T>(ushort[] expected, int depth, int filter, int width, int height, int startX, int startY, int stepX, int stepY)
            where T : unmanaged, IBinaryInteger<T>
        {
            T[] reference = new T[80 * 67], actual = new T[width * height], workspace = new T[Vp9InterPrediction.WorkspaceLength(width, height, stepY)];
            for (int i = 0; i < reference.Length; i++) { reference[i] = T.CreateTruncating((i * 79 + (i >> 5) * 131 + 37) & ((1 << depth) - 1)); }
            Vp9InterPrediction.Predict<T>(reference, 80, 80, 67, actual, width, width, height, startX, startY, stepX, stepY, filter, depth, workspace, false);
            for (int i = 0; i < actual.Length; i++)
            { Assert.True(expected[i] == int.CreateTruncating(actual[i]), $"Depth {depth}, filter {filter}, size {width}x{height}, origin ({startX},{startY}), step ({stepX},{stepY}), sample {i}: expected {expected[i]}, actual {actual[i]}."); }
            Array.Fill(actual, T.CreateTruncating(37));
            Vp9InterPrediction.Predict<T>(reference, 80, 80, 67, actual, width, width, height, startX, startY, stepX, stepY, filter, depth, workspace, true);
            for (int i = 0; i < actual.Length; i++) { Assert.Equal((expected[i] + 38) >> 1, int.CreateTruncating(actual[i])); }
        }
    }
}
