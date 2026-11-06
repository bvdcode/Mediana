// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkResamplingFilter
    {
        private static readonly short[] AllpassQ16 = [1746, 14986, -26453, 6854, 25769, -9994];
        private static readonly short[] FractionalQ15 =
        [
            189, -600, 617, 30567, 117, -159, -1070, 29704,
            52, 221, -2392, 28276, -4, 529, -3350, 26341,
            -48, 758, -3956, 23973, -80, 905, -4235, 21254,
            -99, 972, -4222, 18278, -107, 967, -3957, 15143,
            -103, 896, -3487, 11950, -91, 773, -2865, 8798,
            -71, 611, -2143, 5784, -46, 425, -1375, 2996
        ];

        public static void Upsample2(ReadOnlySpan<short> input, Span<short> output, Span<int> history)
        {
            for (int at = 0; at < input.Length; at++)
            {
                int original = input[at] << 10;
                for (int phase = 0; phase < 2; phase++)
                {
                    int value = original;
                    for (int section = 0; section < 3; section++)
                    {
                        int index = 3 * phase + section, difference = value - history[index];
                        int correction = (int)((long)difference * AllpassQ16[index] >> 16);
                        if (section == 2) { correction += difference; }
                        int next = history[index] + correction;
                        history[index] = value + correction; value = next;
                    }
                    output[2 * at + phase] = (short)Math.Clamp(SilkFixedMath.RoundShift(value, 10), short.MinValue, short.MaxValue);
                }
            }
        }

        public static int Interpolate(ReadOnlySpan<short> input, int samples, int incrementQ16, Span<short> output)
        {
            int written = 0, limit = samples << 17;
            for (int position = 0; position < limit; position += incrementQ16)
            {
                int phase = ((position & 65535) * 12) >> 16, start = position >> 16, sum = 0;
                for (int tap = 0; tap < 4; tap++)
                {
                    sum = unchecked(sum + input[start + tap] * FractionalQ15[4 * phase + tap]);
                    sum = unchecked(sum + input[start + 7 - tap] * FractionalQ15[4 * (11 - phase) + tap]);
                }
                output[written++] = (short)Math.Clamp(SilkFixedMath.RoundShift(sum, 15), short.MinValue, short.MaxValue);
            }
            return written;
        }
    }
}
