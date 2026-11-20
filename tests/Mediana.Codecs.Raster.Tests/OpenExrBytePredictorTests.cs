// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.OpenExr;

namespace Mediana.Codecs.Raster.Tests
{
    public class OpenExrBytePredictorTests
    {
        [Fact]
        public void PredictorReversesIndependentDeltaAndByteSeparationAcrossVectorBoundaries()
        {
            Random random = new(713);
            foreach (int length in Enumerable.Range(1, 257).Concat([16383, 16384, 16385, 32769]))
            {
                byte[] original = new byte[length]; random.NextBytes(original);
                byte[] separated = new byte[length];
                int target = 0;
                for (int i = 0; i < length; i += 2) { separated[target++] = original[i]; }
                for (int i = 1; i < length; i += 2) { separated[target++] = original[i]; }
                byte[] predicted = separated.ToArray();
                for (int i = 1; i < length; i++) { predicted[i] = unchecked((byte)(separated[i] - separated[i - 1] + 128)); }
                ExrBytePredictor.Restore(predicted, default);
                Assert.Equal(separated, predicted);
                byte[] decoded = new byte[length];
                ExrBytePredictor.Interleave(predicted, decoded, default);
                Assert.Equal(original, decoded);
            }
        }
    }
}
