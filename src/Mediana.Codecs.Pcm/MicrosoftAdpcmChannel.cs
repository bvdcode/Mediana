// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Pcm
{
    internal struct MicrosoftAdpcmChannel(int firstCoefficient, int secondCoefficient, int initialDelta,
        short firstSample, short secondSample)
    {
        private const int CoefficientScale = 256;
        private const int MinimumDelta = 16;
        private const int MaximumDelta = int.MaxValue / 768;
        private static ReadOnlySpan<ushort> Adaptation => [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];
        private int delta = initialDelta;
        private short previous = secondSample;
        private short preceding = firstSample;

        public short Decode(int code)
        {
            int error = code < 8 ? code : code - 16;
            long estimate = ((long)previous * firstCoefficient + (long)preceding * secondCoefficient) / CoefficientScale;
            short sample = (short)Math.Clamp(estimate + error * delta, short.MinValue, short.MaxValue);
            preceding = previous;
            previous = sample;
            delta = Math.Clamp(delta * Adaptation[code] / CoefficientScale, MinimumDelta, MaximumDelta);
            return sample;
        }
    }
}
