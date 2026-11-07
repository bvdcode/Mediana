// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Codecs
{
    internal static class AudioPacketTiming
    {
        public static int GetPresentationSamples(MediaDuration? duration, int sampleRate, int maximum)
        {
            if (duration is null) { return maximum; }
            MediaDuration value = duration.Value;
            Int128 numerator = (Int128)value.Value * value.TimeBase.Numerator * sampleRate;
            Int128 count = numerator / value.TimeBase.Denominator;
            if (numerator % value.TimeBase.Denominator != 0 || count > maximum)
            {
                throw new InvalidDataException("The audio presentation duration must contain a whole number of samples within its coded packet.");
            }
            return (int)count;
        }
    }
}
