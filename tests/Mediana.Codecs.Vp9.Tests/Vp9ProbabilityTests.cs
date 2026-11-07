// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9.Tests
{
    public class Vp9ProbabilityTests
    {
        [Fact]
        public async Task CoefficientAdaptationMatchesIndependentFrameTypeAndCounterCases()
        {
            byte[] fixture = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Data", "coefficient-adaptation.bin"));
            using BinaryReader reader = new(new MemoryStream(fixture)); Assert.Equal(4, reader.ReadInt32());
            for (int trial = 0; trial < 4; trial++)
            {
                bool intra = reader.ReadInt32() != 0, previousKey = reader.ReadInt32() != 0;
                Vp9ProbabilityContext probabilities = new(); probabilities.Reset(); Vp9CoefficientCounts counts = new();
                for (int index = 0; index < 576; index++)
                {
                    for (int token = 0; token < 3; token++) { counts.Tokens[index * 3 + token] = reader.ReadUInt32(); }
                    counts.More[index * 2] = reader.ReadUInt32(); counts.More[index * 2 + 1] = reader.ReadUInt32();
                }
                Vp9ProbabilityAdaptation.Coefficients(probabilities, counts, intra, previousKey);
                Assert.Equal(reader.ReadBytes(1728), probabilities.Coefficient.ToArray());
            }
            Assert.Equal(fixture.Length, reader.BaseStream.Position);
        }

        [Fact]
        public void IntraAdaptationRestoresOnlyTheNormativeProbabilityGroups()
        {
            Vp9ProbabilityContext original = new(), updated = new(); original.Reset(); updated.Reset();
            updated.Data.Fill(17); updated.RestoreForAdaptation(original, true);
            Assert.Equal(original.Coefficient.ToArray(), updated.Coefficient.ToArray());
            Assert.All(updated.Transform.ToArray(), probability => Assert.Equal(17, probability));
            Assert.All(updated.Skip.ToArray(), probability => Assert.Equal(17, probability));
            Assert.Equal(original.Partition.ToArray(), updated.Partition.ToArray());
            Assert.Equal(original.MotionClass.ToArray(), updated.MotionClass.ToArray());
            updated.RestoreForAdaptation(original, false); Assert.Equal(original.Data.ToArray(), updated.Data.ToArray());
        }
    }
}
