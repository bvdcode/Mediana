// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Ac3
{
    internal static class Ac3BitAllocation
    {
        private static ReadOnlySpan<int> SlowDecay => [15, 17, 19, 21];
        private static ReadOnlySpan<int> FastDecay => [63, 83, 103, 123];
        private static ReadOnlySpan<int> SlowGain => [0x540, 0x4d8, 0x478, 0x410];
        private static ReadOnlySpan<int> DbKnee => [0, 0x700, 0x900, 0xb00];
        private static ReadOnlySpan<int> Floors => [0x2f0, 0x2b0, 0x270, 0x230, 0x1f0, 0x170, 0xf0, -2048];
        internal static ReadOnlySpan<int> BandStarts =>
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27,
            28, 31, 34, 37, 40, 43, 46, 49, 55, 61, 67, 73, 79, 85, 97, 109, 121, 133, 157, 181, 205, 229, 253];
        private static ReadOnlySpan<int> LogAddition =>
            [
            64, 63, 62, 61, 60, 59, 58, 57, 56, 55, 54, 53,
            52, 52, 51, 50, 49, 48, 47, 47, 46, 45, 44, 44,
            43, 42, 41, 41, 40, 39, 38, 38, 37, 36, 36, 35,
            35, 34, 33, 33, 32, 32, 31, 30, 30, 29, 29, 28,
            28, 27, 27, 26, 26, 25, 25, 24, 24, 23, 23, 22,
            22, 21, 21, 21, 20, 20, 19, 19, 19, 18, 18, 18,
            17, 17, 17, 16, 16, 16, 15, 15, 15, 14, 14, 14,
            13, 13, 13, 13, 12, 12, 12, 12, 11, 11, 11, 11,
            10, 10, 10, 10, 10, 9, 9, 9, 9, 9, 8, 8,
            8, 8, 8, 8, 7, 7, 7, 7, 7, 7, 6, 6,
            6, 6, 6, 6, 6, 6, 5, 5, 5, 5, 5, 5,
            5, 5, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
            4, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0
            ];
        private static ReadOnlySpan<int> HearingThreshold =>
            [
            1232, 1232, 1088, 1024, 992, 960, 944, 944, 928, 928, 928, 928,
            928, 912, 912, 912, 896, 896, 880, 880, 864, 864, 848, 848,
            832, 832, 816, 800, 784, 768, 752, 752, 752, 752, 768, 784,
            832, 912, 992, 1056, 1120, 1168, 1184, 1120, 1088, 1088, 1312, 2048,
            2112, 2112, 1264, 1264, 1120, 1040, 992, 976, 960, 944, 944, 928,
            928, 928, 928, 928, 912, 912, 912, 896, 896, 896, 880, 880,
            864, 864, 848, 848, 832, 832, 800, 784, 768, 752, 752, 752,
            752, 768, 800, 848, 912, 992, 1056, 1104, 1184, 1168, 1120, 1088,
            1152, 1584, 2112, 2112, 1408, 1408, 1200, 1104, 1056, 1008, 992, 976,
            960, 944, 944, 944, 928, 928, 928, 928, 928, 928, 928, 928,
            912, 912, 912, 912, 896, 896, 896, 880, 864, 848, 832, 816,
            800, 784, 768, 752, 752, 752, 768, 784, 816, 848, 960, 1040,
            1136, 1184, 1120, 1088, 1104, 1248
            ];
        private static ReadOnlySpan<byte> Allocation =>
            [
            0, 1, 1, 1, 1, 1, 2, 2, 3, 3, 3, 4,
            4, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 8,
            8, 8, 8, 9, 9, 9, 9, 10, 10, 10, 10, 11,
            11, 11, 11, 12, 12, 12, 12, 13, 13, 13, 13, 14,
            14, 14, 14, 14, 14, 14, 14, 15, 15, 15, 15, 15,
            15, 15, 15, 15
            ];

        internal static int Band(int bin)
        {
            int index = BandStarts.BinarySearch(bin);
            return index >= 0 ? index : ~index - 1;
        }

        public static void Compute(ReadOnlySpan<byte> exponents, Span<byte> bap, int start, int end,
            int sampleRateCode, int slowDecay, int fastDecay, int slowGain, int dbKnee, int floorCode,
            int snrOffset, int fastGainCode, int couplingFastLeak, int couplingSlowLeak, ReadOnlySpan<int> delta)
        {
            Span<int> psd = stackalloc int[256];
            Span<int> bandPsd = stackalloc int[50];
            Span<int> excitation = stackalloc int[50];
            int firstBand = Band(start), endBand = Band(end - 1) + 1;
            int fGain = (fastGainCode + 1) * 128, sGain = SlowGain[slowGain];
            for (int bin = start; bin < end; bin++) { psd[bin] = 3072 - (exponents[bin] << 7); }
            for (int band = firstBand, bin = start; band < endBand; band++)
            {
                int sum = psd[bin++], limit = Math.Min(BandStarts[band + 1], end);
                while (bin < limit)
                {
                    int value = psd[bin++];
                    sum = Math.Max(sum, value) + LogAddition[Math.Min(Math.Abs(sum - value) >> 1, 255)];
                }
                bandPsd[band] = sum;
            }
            int fastLeak = couplingFastLeak * 256 + 768, slowLeak = couplingSlowLeak * 256 + 768;
            int begin = firstBand, low = 0;
            if (firstBand == 0)
            {
                low = LowComp(low, bandPsd[0], bandPsd[1], 0); excitation[0] = bandPsd[0] - fGain - low;
                low = LowComp(low, bandPsd[1], bandPsd[2], 1); excitation[1] = bandPsd[1] - fGain - low;
                begin = 7;
                for (int band = 2; band < 7; band++)
                {
                    if (endBand != 7 || band != 6) { low = LowComp(low, bandPsd[band], bandPsd[band + 1], band); }
                    fastLeak = bandPsd[band] - fGain; slowLeak = bandPsd[band] - sGain;
                    excitation[band] = fastLeak - low;
                    if ((endBand != 7 || band != 6) && bandPsd[band] <= bandPsd[band + 1]) { begin = band + 1; break; }
                }
                for (int band = begin; band < Math.Min(endBand, 22); band++)
                {
                    if (endBand != 7 || band != 6) { low = LowComp(low, bandPsd[band], bandPsd[band + 1], band); }
                    fastLeak = Math.Max(fastLeak - FastDecay[fastDecay], bandPsd[band] - fGain);
                    slowLeak = Math.Max(slowLeak - SlowDecay[slowDecay], bandPsd[band] - sGain);
                    excitation[band] = Math.Max(fastLeak - low, slowLeak);
                }
                begin = 22;
            }
            for (int band = begin; band < endBand; band++)
            {
                fastLeak = Math.Max(fastLeak - FastDecay[fastDecay], bandPsd[band] - fGain);
                slowLeak = Math.Max(slowLeak - SlowDecay[slowDecay], bandPsd[band] - sGain);
                excitation[band] = Math.Max(fastLeak, slowLeak);
            }
            for (int band = firstBand, bin = start; band < endBand; band++)
            {
                int excite = excitation[band];
                if (bandPsd[band] < DbKnee[dbKnee]) { excite += (DbKnee[dbKnee] - bandPsd[band]) >> 2; }
                int mask = Math.Max(excite, HearingThreshold[sampleRateCode * 50 + band]) + delta[band];
                int floor = Floors[floorCode];
                mask = (Math.Max(mask - snrOffset - floor, 0) & 0x1fe0) + floor;
                int limit = Math.Min(BandStarts[band + 1], end);
                for (; bin < limit; bin++) { bap[bin] = Allocation[Math.Clamp((psd[bin] - mask) >> 5, 0, 63)]; }
            }
        }

        private static int LowComp(int value, int first, int second, int band)
        {
            if (band >= 20) { return Math.Max(0, value - 128); }
            if (first + 256 == second) { return band < 7 ? 384 : 320; }
            return first > second ? Math.Max(0, value - 64) : value;
        }
    }
}
