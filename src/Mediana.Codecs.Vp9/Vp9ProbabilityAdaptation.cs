// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9ProbabilityAdaptation
    {
        internal static byte Merge(int previous, uint zero, uint one, int saturation, int update)
        {
            ulong total = (ulong)zero + one;
            if (total == 0) { return (byte)previous; }
            int probability = Math.Clamp((int)(((ulong)zero * 256 + (total >> 1)) / total), 1, 255);
            int factor = update * (int)Math.Min((ulong)saturation, total) / saturation;
            return (byte)((previous * (256 - factor) + probability * factor + 128) >> 8);
        }

        internal static void Coefficients(Vp9ProbabilityContext probabilities, Vp9CoefficientCounts counts, bool intra, bool previousKey)
        {
            int update = !intra && previousKey ? 128 : 112;
            for (int index = 0; index < counts.More.Length / 2; index++)
            {
                int offset = index * 3;
                probabilities.Coefficient[offset] = Merge(probabilities.Coefficient[offset], counts.More[index * 2], counts.More[index * 2 + 1], 24, update);
                probabilities.Coefficient[offset + 1] = Merge(probabilities.Coefficient[offset + 1], counts.Tokens[offset], counts.Tokens[offset + 1] + counts.Tokens[offset + 2], 24, update);
                probabilities.Coefficient[offset + 2] = Merge(probabilities.Coefficient[offset + 2], counts.Tokens[offset + 1], counts.Tokens[offset + 2], 24, update);
            }
        }

        internal static void Symbols(Vp9ProbabilityContext probabilities, Vp9SymbolCounts counts, bool highPrecision)
        {
            Span<byte> values = probabilities.Data;
            for (int index = Vp9ProbabilityOffset.Transform; index < (highPrecision ? values.Length : Vp9ProbabilityOffset.MotionClassZeroHigh); index++)
            { values[index] = Merge(values[index], counts.Branches[index * 2], counts.Branches[index * 2 + 1], 20, 128); }
        }
    }
}
