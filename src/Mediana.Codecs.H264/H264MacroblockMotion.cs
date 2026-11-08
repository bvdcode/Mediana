// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly record struct H264MacroblockMotion(int X, int Y, int DifferenceX, int DifferenceY,
        int NeighbourX, int NeighbourY)
    {
        public static H264MacroblockMotion Create(H264Picture target, H264CabacPredictionWindow window,
            int x, int y, int motionX, int motionY)
        {
            (int predictedX, int predictedY) = H264MotionPrediction.Predict(target, H264ReferenceList.L0, x, y, 16, 16, 0, 0);
            return new(motionX, motionY, motionX - predictedX, motionY - predictedY,
                NeighbourSum(target, window, x, y, false), NeighbourSum(target, window, x, y, true));
        }

        private static int NeighbourSum(H264Picture target, H264CabacPredictionWindow motion,
            int x, int y, bool vertical)
        {
            Span<short> history = vertical ? motion.MotionY(H264ReferenceList.L0) : motion.MotionX(H264ReferenceList.L0);
            int sum = target.IsInCurrentSlice(x - 1, y) ? Math.Abs((int)history[motion.BlockIndex(x - 1, y)]) : 0;
            if (target.IsInCurrentSlice(x, y - 1)) { sum += Math.Abs((int)history[motion.BlockIndex(x, y - 1)]); }
            return sum;
        }

        public static void Copy(H264CabacPredictionWindow motion, int x, int y, Span<short> saved, bool restore)
        {
            Span<short> horizontal = motion.MotionX(H264ReferenceList.L0), vertical = motion.MotionY(H264ReferenceList.L0);
            int offset = 0;
            for (int row = 0; row < 16; row += 4)
            {
                for (int column = 0; column < 16; column += 4)
                {
                    int index = motion.BlockIndex(x + column, y + row);
                    if (restore) { horizontal[index] = saved[offset]; vertical[index] = saved[16 + offset]; }
                    else { saved[offset] = horizontal[index]; saved[16 + offset] = vertical[index]; }
                    offset++;
                }
            }
        }
    }
}
