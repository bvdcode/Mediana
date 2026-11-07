// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal static class Vp9ProbabilityOffset
    {
        internal const int Coefficient = 0;
        internal const int Transform = 1728;
        internal const int Partition = 1752;
        internal const int Luma = 1800;
        internal const int Chroma = 1836;
        internal const int Skip = 1926;
        internal const int IsInter = 1929;
        internal const int CompoundMode = 1933;
        internal const int CompoundReference = 1938;
        internal const int SingleReference = 1943;
        internal const int InterMode = 1953;
        internal const int Interpolation = 1974;
        internal const int MotionJoint = 1982;
        internal const int MotionSign = 1985;
        internal const int MotionClass = 1987;
        internal const int MotionClassZeroBit = 2007;
        internal const int MotionBits = 2009;
        internal const int MotionClassZeroFraction = 2029;
        internal const int MotionFraction = 2041;
        internal const int MotionClassZeroHigh = 2047;
        internal const int MotionHigh = 2049;
    }
}
