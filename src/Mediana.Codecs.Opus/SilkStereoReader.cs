// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class SilkStereoReader
    {
        public static SilkStereoPrediction ReadPrediction(ref OpusRangeDecoder decoder)
        {
            int joint = decoder.ReadInverseCdf([249, 247, 246, 245, 244, 234, 210, 202, 201, 200, 197, 174,
                82, 59, 56, 55, 54, 46, 22, 12, 11, 10, 9, 7, 0]);
            int low = ReadCoefficient(ref decoder, joint / 5);
            int high = ReadCoefficient(ref decoder, joint % 5);
            return new(low - high, high);
        }

        public static bool ReadMidOnly(ref OpusRangeDecoder decoder) => decoder.ReadInverseCdf([64, 0]) != 0;

        private static int ReadCoefficient(ref OpusRangeDecoder decoder, int region)
        {
            int index = 3 * region + decoder.ReadInverseCdf([171, 85, 0]);
            int subdivision = decoder.ReadInverseCdf([205, 154, 102, 51, 0]);
            int lower = Quantization[index], step = (Quantization[index + 1] - lower) * 6554 >> 16;
            return lower + step * (2 * subdivision + 1);
        }

        private static readonly short[] Quantization =
            [-13732, -10050, -8266, -7526, -6500, -5000, -2950, -820, 820, 2950, 5000, 6500, 7526, 8266, 10050, 13732];
    }
}
