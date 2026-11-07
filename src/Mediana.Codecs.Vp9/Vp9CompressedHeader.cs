// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal class Vp9CompressedHeader
    {
        internal int TransformMode { get; private set; }
        internal int ReferenceMode { get; private set; }
        internal int FixedReference { get; private set; }
        internal int VariableReferenceZero { get; private set; }
        internal int VariableReferenceOne { get; private set; }

        internal static Vp9CompressedHeader Parse(ReadOnlySpan<byte> encoded, Vp9FrameHeader header, Vp9ProbabilityContext probabilities)
        {
            Vp9CompressedHeader result = new();
            Vp9BooleanReader bits = new(encoded);
            result.Read(ref bits, header, probabilities); bits.Finish(); return result;
        }

        private void Read(ref Vp9BooleanReader bits, Vp9FrameHeader header, Vp9ProbabilityContext probabilities)
        {
            if (!header.Lossless)
            {
                TransformMode = bits.Literal(2);
                if (TransformMode == 3) { TransformMode += bits.Literal(1); }
            }
            if (TransformMode == 4)
            {
                for (int size = 1; size < 4; size++)
                { for (int context = 0; context < 2; context++) { Vp9ProbabilityUpdate.Diff(ref bits, probabilities.Transform.Slice(size * 6 + context * 3, size)); } }
            }
            int maxTransform = Math.Min(TransformMode, 3);
            for (int size = 0; size <= maxTransform; size++)
            {
                if (!bits.Read()) { continue; }
                for (int plane = 0; plane < 2; plane++)
                {
                    for (int reference = 0; reference < 2; reference++)
                    {
                        for (int band = 0; band < 6; band++)
                        {
                            int offset = ((((size * 2 + plane) * 2 + reference) * 6 + band) * 6) * 3;
                            Vp9ProbabilityUpdate.Diff(ref bits, probabilities.Coefficient.Slice(offset, band == 0 ? 9 : 18));
                        }
                    }
                }
            }
            Vp9ProbabilityUpdate.Diff(ref bits, probabilities.Skip);
            if (header.IsIntra) { return; }
            Vp9ProbabilityUpdate.Diff(ref bits, probabilities.InterMode);
            if (header.InterpolationFilter == 4) { Vp9ProbabilityUpdate.Diff(ref bits, probabilities.Interpolation); }
            Vp9ProbabilityUpdate.Diff(ref bits, probabilities.IsInter);
            ReadReferenceMode(ref bits, header);
            if (ReferenceMode == 2) { Vp9ProbabilityUpdate.Diff(ref bits, probabilities.CompoundMode); }
            if (ReferenceMode != 1) { Vp9ProbabilityUpdate.Diff(ref bits, probabilities.SingleReference); }
            if (ReferenceMode != 0) { Vp9ProbabilityUpdate.Diff(ref bits, probabilities.CompoundReference); }
            Vp9ProbabilityUpdate.Diff(ref bits, probabilities.Luma);
            Vp9ProbabilityUpdate.Diff(ref bits, probabilities.Partition);
            ReadMotion(ref bits, probabilities, header.HighPrecisionMotion);
        }

        private void ReadReferenceMode(ref Vp9BooleanReader bits, Vp9FrameHeader header)
        {
            bool allowed = header.SignBias[2] != header.SignBias[1] || header.SignBias[3] != header.SignBias[1];
            if (!allowed || !bits.Read()) { return; }
            ReferenceMode = bits.Read() ? 2 : 1;
            if (header.SignBias[1] == header.SignBias[2])
            { FixedReference = 3; VariableReferenceZero = 1; VariableReferenceOne = 2; }
            else if (header.SignBias[1] == header.SignBias[3])
            { FixedReference = 2; VariableReferenceZero = 1; VariableReferenceOne = 3; }
            else
            { FixedReference = 1; VariableReferenceZero = 2; VariableReferenceOne = 3; }
        }

        private static void ReadMotion(ref Vp9BooleanReader bits, Vp9ProbabilityContext probabilities, bool highPrecision)
        {
            Vp9ProbabilityUpdate.Motion(ref bits, probabilities.MotionJoint);
            for (int component = 0; component < 2; component++)
            {
                Vp9ProbabilityUpdate.Motion(ref bits, probabilities.MotionSign.Slice(component, 1));
                Vp9ProbabilityUpdate.Motion(ref bits, probabilities.MotionClass.Slice(component * 10, 10));
                Vp9ProbabilityUpdate.Motion(ref bits, probabilities.MotionClassZeroBit.Slice(component, 1));
                Vp9ProbabilityUpdate.Motion(ref bits, probabilities.MotionBits.Slice(component * 10, 10));
            }
            for (int component = 0; component < 2; component++)
            {
                Vp9ProbabilityUpdate.Motion(ref bits, probabilities.MotionClassZeroFraction.Slice(component * 6, 6));
                Vp9ProbabilityUpdate.Motion(ref bits, probabilities.MotionFraction.Slice(component * 3, 3));
            }
            if (!highPrecision) { return; }
            for (int component = 0; component < 2; component++)
            {
                Vp9ProbabilityUpdate.Motion(ref bits, probabilities.MotionClassZeroHigh.Slice(component, 1));
                Vp9ProbabilityUpdate.Motion(ref bits, probabilities.MotionHigh.Slice(component, 1));
            }
        }
    }
}
