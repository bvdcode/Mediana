// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9MotionReader
    {
        internal static Vp9MotionVector Read(ref Vp9BooleanReader bits, Vp9ProbabilityContext probabilities,
            Vp9SymbolCounts counts, Vp9MotionVector prediction, bool highPrecision)
        {
            bool useHigh = highPrecision && prediction.HighPrecision;
            int joint = counts.Tree(ref bits, JointTree, probabilities, Vp9ProbabilityOffset.MotionJoint);
            int y = joint is 2 or 3 ? Component(ref bits, probabilities, counts, 0, useHigh) : 0;
            int x = joint is 1 or 3 ? Component(ref bits, probabilities, counts, 1, useHigh) : 0;
            return prediction.Add(new(x, y));
        }

        private static int Component(ref Vp9BooleanReader bits, Vp9ProbabilityContext probabilities, Vp9SymbolCounts counts, int component, bool high)
        {
            bool negative = counts.Read(ref bits, probabilities, Vp9ProbabilityOffset.MotionSign + component);
            int motionClass = counts.Tree(ref bits, ClassTree, probabilities, Vp9ProbabilityOffset.MotionClass + component * 10);
            int value, fraction, hp = 1;
            if (motionClass == 0)
            {
                int bit = counts.Read(ref bits, probabilities, Vp9ProbabilityOffset.MotionClassZeroBit + component) ? 1 : 0;
                fraction = counts.Tree(ref bits, JointTree, probabilities, Vp9ProbabilityOffset.MotionClassZeroFraction + component * 6 + bit * 3);
                if (high) { hp = counts.Read(ref bits, probabilities, Vp9ProbabilityOffset.MotionClassZeroHigh + component) ? 1 : 0; }
                else { counts.Record(Vp9ProbabilityOffset.MotionClassZeroHigh + component, true); }
                value = bit * 8 + fraction * 2 + hp + 1;
            }
            else
            {
                int offset = 0;
                for (int i = 0; i < motionClass; i++)
                { if (counts.Read(ref bits, probabilities, Vp9ProbabilityOffset.MotionBits + component * 10 + i)) { offset |= 1 << i; } }
                fraction = counts.Tree(ref bits, JointTree, probabilities, Vp9ProbabilityOffset.MotionFraction + component * 3);
                if (high) { hp = counts.Read(ref bits, probabilities, Vp9ProbabilityOffset.MotionHigh + component) ? 1 : 0; }
                else { counts.Record(Vp9ProbabilityOffset.MotionHigh + component, true); }
                value = (2 << (motionClass + 2)) + offset * 8 + fraction * 2 + hp + 1;
            }
            return negative ? -value : value;
        }

        private static ReadOnlySpan<sbyte> JointTree => [0, 2, -1, 4, -2, -3];
        private static ReadOnlySpan<sbyte> ClassTree => [0, 2, -1, 4, 6, 8, -2, -3, 10, 12, -4, -5, -6, 14, 16, 18, -7, -8, -9, -10];
    }
}
