// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkNlsfStabilizer
    {
        private const int MaximumRelaxationPasses = 20;
        private const int FrequencyScale = 32768;

        public static void Apply(Span<short> frequencies, ReadOnlySpan<short> distances)
        {
            if (frequencies.Length is not (10 or 16)) { throw new ArgumentException("SILK requires ten or sixteen LSFs.", nameof(frequencies)); }
            ArgumentOutOfRangeException.ThrowIfLessThan(distances.Length, frequencies.Length + 1, nameof(distances));
            foreach (short distance in distances[..(frequencies.Length + 1)])
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(distance, nameof(distances));
            }
            for (int pass = 0; pass < MaximumRelaxationPasses; pass++)
            {
                int minimum = frequencies[0] - distances[0], index = 0;
                for (int at = 1; at <= frequencies.Length; at++)
                {
                    int difference;
                    if (at == frequencies.Length) { difference = FrequencyScale - frequencies[at - 1] - distances[at]; }
                    else { difference = frequencies[at] - frequencies[at - 1] - distances[at]; }
                    if (difference < minimum) { minimum = difference; index = at; }
                }
                if (minimum >= 0) { return; }
                if (index == 0) { frequencies[0] = distances[0]; }
                else if (index == frequencies.Length) { frequencies[^1] = (short)(FrequencyScale - distances[index]); }
                else
                {
                    int low = distances[index] / 2, high = FrequencyScale - distances[index] / 2;
                    for (int at = 0; at < index; at++) { low += distances[at]; }
                    for (int at = frequencies.Length; at > index; at--) { high -= distances[at]; }
                    int center = Math.Clamp((frequencies[index - 1] + frequencies[index] + 1) >> 1, low, high);
                    frequencies[index - 1] = (short)(center - distances[index] / 2);
                    frequencies[index] = (short)(frequencies[index - 1] + distances[index]);
                }
            }
            frequencies.Sort();
            frequencies[0] = Math.Max(frequencies[0], distances[0]);
            for (int at = 1; at < frequencies.Length; at++)
            {
                frequencies[at] = (short)Math.Max(frequencies[at], Math.Min(short.MaxValue, frequencies[at - 1] + distances[at]));
            }
            frequencies[^1] = (short)Math.Min(frequencies[^1], FrequencyScale - distances[frequencies.Length]);
            for (int at = frequencies.Length - 2; at >= 0; at--)
            {
                frequencies[at] = (short)Math.Min(frequencies[at], frequencies[at + 1] - distances[at + 1]);
            }
        }
    }
}
