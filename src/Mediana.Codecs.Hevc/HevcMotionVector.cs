// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    /// <summary>A signed sixteen-bit motion vector in quarter-luma-sample units.</summary>
    internal readonly record struct HevcMotionVector(short X, short Y)
    {
        public HevcMotionVector Add(int x, int y) => new(unchecked((short)(X + x)), unchecked((short)(Y + y)));

        /// <summary>Scales a short-term predictor using H.265 clause 8.5.3.2.</summary>
        public HevcMotionVector Scale(int currentDistance, int referenceDistance)
        {
            int td = Math.Clamp(referenceDistance, -128, 127), tb = Math.Clamp(currentDistance, -128, 127);
            if (td == 0) { throw new InvalidDataException("An HEVC temporal motion reference has zero picture distance."); }
            if (td == tb) { return this; }
            int tx = (16384 + Math.Abs(td / 2)) / td;
            int factor = Math.Clamp((tb * tx + 32) >> 6, -4096, 4095);
            return new(ScaleComponent(X, factor), ScaleComponent(Y, factor));
        }

        private static short ScaleComponent(short value, int factor)
        {
            int product = value * factor;
            int scaled = (product + 127 + (product < 0 ? 1 : 0)) >> 8;
            return (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
        }
    }
}
