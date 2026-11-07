// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal class Vp9ProbabilityContext
    {
        private readonly byte[] data = new byte[2051];
        internal Span<byte> Coefficient => data.AsSpan(0, 1728);
        internal Span<byte> Transform => data.AsSpan(1728, 24);
        internal Span<byte> Partition => data.AsSpan(1752, 48);
        internal Span<byte> Luma => data.AsSpan(1800, 36);
        internal Span<byte> Chroma => data.AsSpan(1836, 90);
        internal Span<byte> Skip => data.AsSpan(1926, 3);
        internal Span<byte> IsInter => data.AsSpan(1929, 4);
        internal Span<byte> CompoundMode => data.AsSpan(1933, 5);
        internal Span<byte> CompoundReference => data.AsSpan(1938, 5);
        internal Span<byte> SingleReference => data.AsSpan(1943, 10);
        internal Span<byte> InterMode => data.AsSpan(1953, 21);
        internal Span<byte> Interpolation => data.AsSpan(1974, 8);
        internal Span<byte> MotionJoint => data.AsSpan(1982, 3);
        internal Span<byte> MotionSign => data.AsSpan(1985, 2);
        internal Span<byte> MotionClass => data.AsSpan(1987, 20);
        internal Span<byte> MotionClassZeroBit => data.AsSpan(2007, 2);
        internal Span<byte> MotionBits => data.AsSpan(2009, 20);
        internal Span<byte> MotionClassZeroFraction => data.AsSpan(2029, 12);
        internal Span<byte> MotionFraction => data.AsSpan(2041, 6);
        internal Span<byte> MotionClassZeroHigh => data.AsSpan(2047, 2);
        internal Span<byte> MotionHigh => data.AsSpan(2049, 2);

        internal void Reset()
        {
            Vp9Tables.DefaultCoefficient.CopyTo(Coefficient);
            Vp9Tables.DefaultTransform.CopyTo(Transform);
            Vp9Tables.DefaultPartition.CopyTo(Partition);
            Vp9Tables.DefaultLuma.CopyTo(Luma);
            Vp9Tables.DefaultChroma.CopyTo(Chroma);
            Vp9Tables.DefaultSkip.CopyTo(Skip);
            Vp9Tables.DefaultIsInter.CopyTo(IsInter);
            Vp9Tables.DefaultCompoundMode.CopyTo(CompoundMode);
            Vp9Tables.DefaultCompoundReference.CopyTo(CompoundReference);
            Vp9Tables.DefaultSingleReference.CopyTo(SingleReference);
            Vp9Tables.DefaultInterMode.CopyTo(InterMode);
            Vp9Tables.DefaultInterpolation.CopyTo(Interpolation);
            Vp9Tables.DefaultMotionJoint.CopyTo(MotionJoint);
            Vp9Tables.DefaultMotionSign.CopyTo(MotionSign);
            Vp9Tables.DefaultMotionClass.CopyTo(MotionClass);
            Vp9Tables.DefaultMotionClassZeroBit.CopyTo(MotionClassZeroBit);
            Vp9Tables.DefaultMotionBits.CopyTo(MotionBits);
            Vp9Tables.DefaultMotionClassZeroFraction.CopyTo(MotionClassZeroFraction);
            Vp9Tables.DefaultMotionFraction.CopyTo(MotionFraction);
            Vp9Tables.DefaultMotionClassZeroHigh.CopyTo(MotionClassZeroHigh);
            Vp9Tables.DefaultMotionHigh.CopyTo(MotionHigh);
        }

        internal void CopyFrom(Vp9ProbabilityContext source) => source.data.CopyTo(data, 0);
    }
}
