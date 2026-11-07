// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal class Vp9ProbabilityContext
    {
        private readonly byte[] data = new byte[2051];
        internal Span<byte> Coefficient => data.AsSpan(Vp9ProbabilityOffset.Coefficient, 1728);
        internal Span<byte> Transform => data.AsSpan(Vp9ProbabilityOffset.Transform, 24);
        internal Span<byte> Partition => data.AsSpan(Vp9ProbabilityOffset.Partition, 48);
        internal Span<byte> Luma => data.AsSpan(Vp9ProbabilityOffset.Luma, 36);
        internal Span<byte> Chroma => data.AsSpan(Vp9ProbabilityOffset.Chroma, 90);
        internal Span<byte> Skip => data.AsSpan(Vp9ProbabilityOffset.Skip, 3);
        internal Span<byte> IsInter => data.AsSpan(Vp9ProbabilityOffset.IsInter, 4);
        internal Span<byte> CompoundMode => data.AsSpan(Vp9ProbabilityOffset.CompoundMode, 5);
        internal Span<byte> CompoundReference => data.AsSpan(Vp9ProbabilityOffset.CompoundReference, 5);
        internal Span<byte> SingleReference => data.AsSpan(Vp9ProbabilityOffset.SingleReference, 10);
        internal Span<byte> InterMode => data.AsSpan(Vp9ProbabilityOffset.InterMode, 21);
        internal Span<byte> Interpolation => data.AsSpan(Vp9ProbabilityOffset.Interpolation, 8);
        internal Span<byte> MotionJoint => data.AsSpan(Vp9ProbabilityOffset.MotionJoint, 3);
        internal Span<byte> MotionSign => data.AsSpan(Vp9ProbabilityOffset.MotionSign, 2);
        internal Span<byte> MotionClass => data.AsSpan(Vp9ProbabilityOffset.MotionClass, 20);
        internal Span<byte> MotionClassZeroBit => data.AsSpan(Vp9ProbabilityOffset.MotionClassZeroBit, 2);
        internal Span<byte> MotionBits => data.AsSpan(Vp9ProbabilityOffset.MotionBits, 20);
        internal Span<byte> MotionClassZeroFraction => data.AsSpan(Vp9ProbabilityOffset.MotionClassZeroFraction, 12);
        internal Span<byte> MotionFraction => data.AsSpan(Vp9ProbabilityOffset.MotionFraction, 6);
        internal Span<byte> MotionClassZeroHigh => data.AsSpan(Vp9ProbabilityOffset.MotionClassZeroHigh, 2);
        internal Span<byte> MotionHigh => data.AsSpan(Vp9ProbabilityOffset.MotionHigh, 2);

        internal Span<byte> Data => data;

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

        internal void RestoreForAdaptation(Vp9ProbabilityContext source, bool intra)
        {
            if (!intra) { CopyFrom(source); return; }
            source.data.AsSpan(0, Vp9ProbabilityOffset.Transform).CopyTo(data);
            source.data.AsSpan(Vp9ProbabilityOffset.Partition, Vp9ProbabilityOffset.Skip - Vp9ProbabilityOffset.Partition).CopyTo(data.AsSpan(Vp9ProbabilityOffset.Partition));
            source.data.AsSpan(Vp9ProbabilityOffset.IsInter).CopyTo(data.AsSpan(Vp9ProbabilityOffset.IsInter));
        }

        internal void CopyFrom(Vp9ProbabilityContext source) => source.data.CopyTo(data, 0);
    }
}
