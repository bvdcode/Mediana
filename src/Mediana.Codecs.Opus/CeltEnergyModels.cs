// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal static class CeltEnergyModels
    {
        public const int Bands = 21;
        public const int MaximumFineBits = 8;

        public static ReadOnlySpan<byte> GetProbabilities(int frameShift, bool intra)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(frameShift);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(frameShift, 3);
            int prediction = 0;
            if (intra) { prediction = 1; }
            return Probabilities.Slice((frameShift * 2 + prediction) * Bands * 2, Bands * 2);
        }

        public static float GetTimePrediction(int frameShift) => frameShift switch
        {
            0 => 29440f / 32768,
            1 => 26112f / 32768,
            2 => 21248f / 32768,
            3 => 16384f / 32768,
            _ => throw new ArgumentOutOfRangeException(nameof(frameShift))
        };

        public static float GetFrequencyPrediction(int frameShift) => frameShift switch
        {
            0 => 30147f / 32768,
            1 => 22282f / 32768,
            2 => 12124f / 32768,
            3 => 6554f / 32768,
            _ => throw new ArgumentOutOfRangeException(nameof(frameShift))
        };

        private static ReadOnlySpan<byte> Probabilities =>
        [
             72,127, 65,129, 66,128, 65,128, 64,128, 62,128, 64,128,
             64,128, 92, 78, 92, 79, 92, 78, 90, 79,116, 41,115, 40,
            114, 40,132, 26,132, 26,145, 17,161, 12,176, 10,177, 11,
             24,179, 48,138, 54,135, 54,132, 53,134, 56,133, 55,132,
             55,132, 61,114, 70, 96, 74, 88, 75, 88, 87, 74, 89, 66,
             91, 67,100, 59,108, 50,120, 40,122, 37, 97, 43, 78, 50,
             83, 78, 84, 81, 88, 75, 86, 74, 87, 71, 90, 73, 93, 74,
             93, 74,109, 40,114, 36,117, 34,117, 34,143, 17,145, 18,
            146, 19,162, 12,165, 10,178,  7,189,  6,190,  8,177,  9,
             23,178, 54,115, 63,102, 66, 98, 69, 99, 74, 89, 71, 91,
             73, 91, 78, 89, 86, 80, 92, 66, 93, 64,102, 59,103, 60,
            104, 60,117, 52,123, 44,138, 35,133, 31, 97, 38, 77, 45,
             61, 90, 93, 60,105, 42,107, 41,110, 45,116, 38,113, 38,
            112, 38,124, 26,132, 27,136, 19,140, 20,155, 14,159, 16,
            158, 18,170, 13,177, 10,187,  8,192,  6,175,  9,159, 10,
             21,178, 59,110, 71, 86, 75, 85, 84, 83, 91, 66, 88, 73,
             87, 72, 92, 75, 98, 72,105, 58,107, 54,115, 52,114, 55,
            112, 56,129, 51,132, 40,150, 33,140, 29, 98, 35, 77, 42,
             42,121, 96, 66,108, 43,111, 40,117, 44,123, 32,120, 36,
            119, 33,127, 33,134, 34,139, 21,147, 23,152, 20,158, 25,
            154, 26,166, 21,173, 16,184, 13,184, 10,150, 13,139, 15,
             22,178, 63,114, 74, 82, 84, 83, 92, 82,103, 62, 96, 72,
             96, 67,101, 73,107, 72,113, 55,118, 52,125, 52,118, 52,
            117, 55,135, 49,137, 39,157, 32,145, 29, 97, 33, 77, 40
        ];
    }
}
