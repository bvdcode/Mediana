// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg4Visual.Tests
{
    public class MotionCompensationTests
    {
        [Theory]
        [InlineData(8)]
        [InlineData(16)]
        public void EveryHalfPixelRoundingAndCompoundModeMatchesTheSampleFormulaAtEdgesAndInside(int size)
        {
            const int width = 48, height = 48;
            Random random = new(8129);
            byte[] reference = new byte[width * height], initial = new byte[width * height];
            random.NextBytes(reference); random.NextBytes(initial);
            foreach (int position in new[] { 0, 16, 32 })
            {
                for (int my = -5; my <= 5; my++)
                {
                    for (int mx = -5; mx <= 5; mx++)
                    {
                        foreach (bool rounding in new[] { false, true })
                        {
                            foreach (bool average in new[] { false, true })
                            {
                                byte[] actual = initial.ToArray(), expected = initial.ToArray();
                                Mpeg4MotionCompensation.PredictHalf(actual, reference, width, height, position, position, size, size, mx, my, rounding, average);
                                for (int y = 0; y < size; y++)
                                {
                                    for (int x = 0; x < size; x++)
                                    {
                                        int sum = 0, count = 0;
                                        for (int vertical = 0; vertical <= (my & 1); vertical++)
                                        {
                                            for (int horizontal = 0; horizontal <= (mx & 1); horizontal++)
                                            {
                                                int rx = Math.Clamp(position + x + (mx >> 1) + horizontal, 0, width - 1);
                                                int ry = Math.Clamp(position + y + (my >> 1) + vertical, 0, height - 1);
                                                sum += reference[ry * width + rx]; count++;
                                            }
                                        }
                                        int value = (sum + count / 2 - (rounding && count > 1 ? 1 : 0)) / count;
                                        int at = (position + y) * width + position + x;
                                        expected[at] = (byte)(average ? (initial[at] + value + 1) / 2 : value);
                                    }
                                }
                                Assert.Equal(expected, actual);
                            }
                        }
                    }
                }
            }
        }
    }
}
