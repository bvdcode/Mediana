// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9MotionTests
    {
        [Fact]
        public async Task MotionVectorsAndAdaptationMatchIndependentEncoderAndCounters()
        {
            byte[] fixture = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "motion.bin"));
            Verify(fixture);
        }

        private static void Verify(byte[] fixture)
        {
            using BinaryReader reader = new(new MemoryStream(fixture)); Assert.Equal(2, reader.ReadInt32());
            for (int high = 0; high < 2; high++)
            {
                int count = reader.ReadInt32(); Assert.Equal(2048, count);
                Vp9MotionVector[] predictions = new Vp9MotionVector[count], expected = new Vp9MotionVector[count];
                for (int i = 0; i < count; i++)
                { predictions[i] = new(reader.ReadInt32(), reader.ReadInt32()); expected[i] = new(reader.ReadInt32(), reader.ReadInt32()); }
                int length = reader.ReadInt32(); byte[] encoded = reader.ReadBytes(length);
                Vp9BooleanReader bits = new(encoded); Vp9ProbabilityContext probabilities = new(); probabilities.Reset(); Vp9SymbolCounts counts = new();
                for (int i = 0; i < count; i++)
                { Assert.Equal(expected[i], Vp9MotionReader.Read(ref bits, probabilities, counts, predictions[i], high != 0)); }
                bits.Finish(); Vp9ProbabilityAdaptation.Symbols(probabilities, counts, high != 0);
                Assert.Equal(reader.ReadBytes(69), probabilities.Data[Vp9ProbabilityOffset.MotionJoint..].ToArray());
            }
            Assert.Equal(fixture.Length, reader.BaseStream.Position);
        }
    }
}
