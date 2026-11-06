// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Drawing
{
    internal ref struct ScanlineSpanEnumerator(ReadOnlySpan<ScanlineCrossing> crossings, FillRule rule)
    {
        private readonly ReadOnlySpan<ScanlineCrossing> crossings = crossings;
        private readonly FillRule rule = rule;
        private int position;
        private int winding;
        private float start;

        internal bool MoveNext(out float left, out float right)
        {
            while (position < crossings.Length)
            {
                ScanlineCrossing crossing = crossings[position++];
                bool wasInside = Inside(winding);
                winding += crossing.Winding;
                bool isInside = Inside(winding);
                if (!wasInside && isInside) { start = crossing.X; }
                else if (wasInside && !isInside) { left = start; right = crossing.X; return true; }
            }
            left = right = 0;
            return false;
        }

        private bool Inside(int value) => rule switch
        {
            FillRule.NonZero => value != 0,
            FillRule.EvenOdd => value % 2 != 0,
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };
    }
}
