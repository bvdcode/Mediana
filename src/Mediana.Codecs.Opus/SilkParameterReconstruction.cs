// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkParameterReconstruction
    {
        public static SilkFilterParameters Reconstruct(SilkFrameMode mode, SilkCodingMode coding, SilkFrameIndices indices,
            SilkIndexBuffers raw, SilkParameterBuffers parameters, Span<short> previousLsf, ref int previousGainIndex, bool firstFrame)
        {
            SilkNlsfCodebook codebook = new(mode.LpcOrder);
            ArgumentOutOfRangeException.ThrowIfLessThan(previousLsf.Length, mode.LpcOrder, nameof(previousLsf));
            ArgumentOutOfRangeException.ThrowIfLessThan(parameters.GainsQ16.Length, mode.Subframes, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfLessThan(parameters.LpcQ12.Length, 2 * mode.LpcOrder, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfLessThan(parameters.PitchLags.Length, mode.Subframes, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfLessThan(parameters.LtpQ14.Length, 5 * mode.Subframes, nameof(parameters));
            ArgumentOutOfRangeException.ThrowIfLessThan(raw.Gains.Length, mode.Subframes, nameof(raw));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(indices.InterpolationQ2, 4, nameof(indices));
            bool conditional = coding switch
            {
                SilkCodingMode.Independent or SilkCodingMode.IndependentWithoutLtpScaling => false,
                SilkCodingMode.Conditional => true,
                _ => throw new ArgumentOutOfRangeException(nameof(coding))
            };
            bool voiced = indices.Signal switch
            {
                SilkSignalType.Inactive or SilkSignalType.Unvoiced => false,
                SilkSignalType.Voiced => true,
                _ => throw new ArgumentOutOfRangeException(nameof(indices))
            };
            SilkGainReader.Dequantize(raw.Gains[..mode.Subframes], parameters.GainsQ16, ref previousGainIndex, conditional);
            Span<short> current = stackalloc short[16], early = stackalloc short[16];
            current = current[..mode.LpcOrder]; early = early[..mode.LpcOrder];
            SilkNlsfReader.Dequantize(codebook, raw.Lsf, current);
            Span<short> earlyLpc = parameters.LpcQ12[..mode.LpcOrder], lateLpc = parameters.LpcQ12.Slice(mode.LpcOrder, mode.LpcOrder);
            SilkLpcConverter.Convert(current, lateLpc);
            byte interpolation = indices.InterpolationQ2;
            if (firstFrame) { interpolation = 4; }
            if (interpolation < 4)
            {
                for (int at = 0; at < mode.LpcOrder; at++)
                {
                    early[at] = (short)(previousLsf[at] + ((current[at] - previousLsf[at]) * interpolation >> 2));
                }
                SilkLpcConverter.Convert(early, earlyLpc);
            }
            else { lateLpc.CopyTo(earlyLpc); }
            current.CopyTo(previousLsf);
            int scale = 0;
            if (voiced) { scale = SilkPitchReader.Dequantize(mode, indices.Pitch, raw.PitchFilters, parameters.PitchLags, parameters.LtpQ14); }
            else
            {
                parameters.PitchLags[..mode.Subframes].Clear();
                parameters.LtpQ14[..(5 * mode.Subframes)].Clear();
            }
            return new(interpolation, scale);
        }
    }
}
