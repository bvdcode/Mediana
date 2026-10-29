// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Applies HEVC section 8.7.2.5 to four rows crossing a block edge.
    /// </summary>
    /// <remarks>
    /// Each luma row contains p3,p2,p1,p0,q0,q1,q2,q3. Row zero and row three determine
    /// the shared decision. Chroma rows contain p1,p0,q0,q1. The caller transposes
    /// horizontal edges into this layout. All decisions use original samples, arithmetic
    /// shifts round negative differences toward negative infinity, and final samples clip
    /// to the component bit depth. Bypass sides participate in decisions but retain samples.
    /// </remarks>
    internal static class DeblockingKernel
    {
        public static void Luma(Span<int> rows, int beta, int tc, int depth, bool writeP, bool writeQ)
        {
            int dp0 = Curvature(rows, 0, false);
            int dp3 = Curvature(rows, 3, false);
            int dq0 = Curvature(rows, 0, true);
            int dq3 = Curvature(rows, 3, true);
            int dp = dp0 + dp3;
            int dq = dq0 + dq3;
            if ((dp + dq) >= beta)
            {
                return;
            }
            bool strong = Strong(rows.Slice(0, 8), 2 * (dp0 + dq0), beta, tc) &&
                Strong(rows.Slice(24, 8), 2 * (dp3 + dq3), beta, tc);
            bool secondP = dp < ((beta + (beta >> 1)) >> 3);
            bool secondQ = dq < ((beta + (beta >> 1)) >> 3);
            for (int row = 0; row < 4; row++)
            {
                Span<int> line = rows.Slice(row * 8, 8);
                if (strong)
                {
                    StrongLine(line, tc, writeP, writeQ);
                }
                else
                {
                    WeakLine(line, tc, depth, secondP, secondQ, writeP, writeQ);
                }
            }
        }

        public static void Chroma(Span<int> row, int tc, int depth, bool writeP, bool writeQ)
        {
            int delta = Clip((((((row[2] - row[1]) << 2) + row[0]) - row[3]) + 4) >> 3, -tc, tc);
            if (writeP)
            {
                row[1] = CoefficientMath.ClipSample(row[1] + delta, depth);
            }
            if (writeQ)
            {
                row[2] = CoefficientMath.ClipSample(row[2] - delta, depth);
            }
        }

        private static int Curvature(ReadOnlySpan<int> rows, int row, bool q)
        {
            int start = row * 8;
            return q ? Math.Abs((rows[start + 6] - (2 * rows[start + 5])) + rows[start + 4]) :
                Math.Abs((rows[start + 1] - (2 * rows[start + 2])) + rows[start + 3]);
        }

        private static bool Strong(ReadOnlySpan<int> row, int dpq, int beta, int tc) =>
            (dpq < (beta >> 2)) && ((Math.Abs(row[0] - row[3]) + Math.Abs(row[4] - row[7])) < (beta >> 3)) &&
            (Math.Abs(row[3] - row[4]) < (((5 * tc) + 1) >> 1));

        private static void StrongLine(Span<int> row, int tc, bool writeP, bool writeQ)
        {
            int p3 = row[0], p2 = row[1], p1 = row[2], p0 = row[3];
            int q0 = row[4], q1 = row[5], q2 = row[6], q3 = row[7];
            int limit = 2 * tc;
            if (writeP)
            {
                row[3] = Clip((p2 + (2 * p1) + (2 * p0) + (2 * q0) + q1 + 4) >> 3, p0 - limit, p0 + limit);
                row[2] = Clip((p2 + p1 + p0 + q0 + 2) >> 2, p1 - limit, p1 + limit);
                row[1] = Clip(((2 * p3) + (3 * p2) + p1 + p0 + q0 + 4) >> 3, p2 - limit, p2 + limit);
            }

            if (writeQ)
            {
                row[4] = Clip((p1 + (2 * p0) + (2 * q0) + (2 * q1) + q2 + 4) >> 3, q0 - limit, q0 + limit);
                row[5] = Clip((p0 + q0 + q1 + q2 + 2) >> 2, q1 - limit, q1 + limit);
                row[6] = Clip((p0 + q0 + q1 + (3 * q2) + (2 * q3) + 4) >> 3, q2 - limit, q2 + limit);
            }
        }

        private static void WeakLine(Span<int> row, int tc, int depth, bool secondP, bool secondQ, bool writeP, bool writeQ)
        {
            int p2 = row[1], p1 = row[2], p0 = row[3];
            int q0 = row[4], q1 = row[5], q2 = row[6];
            int delta = (((9 * (q0 - p0)) - (3 * (q1 - p1))) + 8) >> 4;
            if (Math.Abs(delta) >= (10 * tc))
            {
                return;
            }
            delta = Clip(delta, -tc, tc);
            if (writeP)
            {
                row[3] = CoefficientMath.ClipSample(p0 + delta, depth);
                if (secondP)
                {
                    int change = Clip((((((p2 + p0 + 1) >> 1) - p1) + delta) >> 1), -(tc >> 1), tc >> 1);
                    row[2] = CoefficientMath.ClipSample(p1 + change, depth);
                }
            }

            if (writeQ)
            {
                row[4] = CoefficientMath.ClipSample(q0 - delta, depth);
                if (secondQ)
                {
                    int change = Clip(((((q2 + q0 + 1) >> 1) - q1 - delta) >> 1), -(tc >> 1), tc >> 1);
                    row[5] = CoefficientMath.ClipSample(q1 + change, depth);
                }
            }
        }

        private static int Clip(int value, int minimum, int maximum) => Math.Min(maximum, Math.Max(minimum, value));
    }
}
