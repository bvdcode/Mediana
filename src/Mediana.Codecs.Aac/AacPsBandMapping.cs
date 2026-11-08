// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Aac
{
    internal static class AacPsBandMapping
    {
        public static ReadOnlySpan<sbyte> Parameters(int bands) => bands switch
        {
            20 => Parameters20,
            34 => Parameters34,
            _ => throw new ArgumentOutOfRangeException(nameof(bands))
        };

        public static double CenterFrequency(int band, int stereoBands)
        {
            return stereoBands switch
            {
                20 when band < 10 => Centers20[band],
                20 => band + 0.5 - 7,
                34 when band < 32 => Centers34[band],
                34 => band + 0.5 - 27,
                _ => throw new ArgumentOutOfRangeException(nameof(stereoBands))
            };
        }

        public static void Map<T>(ReadOnlySpan<T> source, int sourceBands, Span<T> destination, int targetBands)
            where T : INumberBase<T>
        {
            destination.Clear();
            if (sourceBands == targetBands)
            {
                source[..sourceBands].CopyTo(destination);
                return;
            }
            if (sourceBands == 10)
            {
                switch (targetBands)
                {
                    case 20:
                        for (int index = 0; index < 20; index++)
                        {
                            destination[index] = source[index / 2];
                        }
                        break;
                    case 34:
                        for (int index = 0; index < 34; index++)
                        {
                            destination[index] = source[IndicesTwentyToThirtyFour[index] / 2];
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(targetBands));
                }
                return;
            }
            switch ((sourceBands, targetBands))
            {
                case (20, 34):
                    MapTwentyToThirtyFour(source, destination);
                    break;
                case (34, 20):
                    MapThirtyFourToTwenty(source, destination);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(sourceBands));
            }
        }

        private static void MapTwentyToThirtyFour<T>(ReadOnlySpan<T> source, Span<T> destination) where T : INumberBase<T>
        {
            ReadOnlySpan<byte> index = IndicesTwentyToThirtyFour;
            for (int band = 0; band < index.Length; band++)
            {
                destination[band] = source[index[band]];
            }
            destination[1] = (source[0] + source[1]) / T.CreateChecked(2);
            destination[4] = (source[2] + source[3]) / T.CreateChecked(2);
        }

        private static ReadOnlySpan<byte> IndicesTwentyToThirtyFour =>
        [
            0, 0, 1, 2, 2, 3, 4, 4, 5, 5, 6, 7, 8, 8, 9, 9, 10, 11, 12, 13, 14, 14,
            15, 15, 16, 16, 17, 17, 18, 18, 18, 18, 19, 19
        ];

        private static void MapThirtyFourToTwenty<T>(ReadOnlySpan<T> source, Span<T> destination) where T : INumberBase<T>
        {
            T two = T.CreateChecked(2);
            T three = T.CreateChecked(3);
            destination[0] = (two * source[0] + source[1]) / three;
            destination[1] = (source[1] + two * source[2]) / three;
            destination[2] = (two * source[3] + source[4]) / three;
            destination[3] = (source[4] + two * source[5]) / three;
            destination[4] = (source[6] + source[7]) / two;
            destination[5] = (source[8] + source[9]) / two;
            destination[6] = source[10];
            destination[7] = source[11];
            destination[8] = (source[12] + source[13]) / two;
            destination[9] = (source[14] + source[15]) / two;
            for (int band = 10; band < 14; band++)
            {
                destination[band] = source[band + 6];
            }
            for (int band = 14; band < 18; band++)
            {
                int first = band * 2 - 8;
                destination[band] = (source[first] + source[first + 1]) / two;
            }
            destination[18] = (source[28] + source[29] + source[30] + source[31]) / T.CreateChecked(4);
            destination[19] = (source[32] + source[33]) / two;
        }

        private static ReadOnlySpan<sbyte> Parameters20 =>
        [
            -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 14, 15, 15, 15,
            16, 16, 16, 16, 17, 17, 17, 17, 17, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18,
            19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19, 19,
            19, 19, 19, 19, 19, 19, 19, 19
        ];

        private static ReadOnlySpan<sbyte> Parameters34 =>
        [
            0, 1, 2, 3, 4, 5, 6, 6, 7, -3, -2, -1, 10, 10, 4, 5, 6, 7, 8, 9, 10, 11, 12, 9,
            14, 11, 12, 13, 14, 15, 16, 13, 16, 17, 18, 19, 20, 21, 22, 22, 23, 23, 24, 24, 25, 25,
            26, 26, 27, 27, 27, 28, 28, 28, 29, 29, 29, 30, 30, 30, 31, 31, 31, 31, 32, 32, 32, 32,
            33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33, 33
        ];

        private static ReadOnlySpan<double> Centers20 => [-3.0 / 8, -1.0 / 8, 1.0 / 8, 3.0 / 8, 5.0 / 8,
            7.0 / 8, 5.0 / 4, 7.0 / 4, 9.0 / 4, 11.0 / 4];

        private static ReadOnlySpan<double> Centers34 => [1.0 / 12, 3.0 / 12, 5.0 / 12, 7.0 / 12, 9.0 / 12, 11.0 / 12,
            13.0 / 12, 15.0 / 12, 17.0 / 12, -5.0 / 12, -3.0 / 12, -1.0 / 12, 17.0 / 8, 19.0 / 8, 5.0 / 8, 7.0 / 8,
            9.0 / 8, 11.0 / 8, 13.0 / 8, 15.0 / 8, 9.0 / 4, 11.0 / 4, 13.0 / 4, 7.0 / 4, 17.0 / 4, 11.0 / 4,
            13.0 / 4, 15.0 / 4, 17.0 / 4, 19.0 / 4, 21.0 / 4, 15.0 / 4];
    }
}
