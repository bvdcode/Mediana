// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.H264
{
    internal struct H264CabacCostWriter : IH264CabacBinWriter
    {
        private int initialRange;
        private long bits;
        public int Range { get; private set; }
        public readonly double Cost => bits + H264CabacRate.LogRange(initialRange) - H264CabacRate.LogRange(Range);

        public void Reset(int range)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(range, 256);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(range, 510);
            initialRange = Range = range;
            bits = 0;
        }

        public void WriteBin(bool value, ref H264CabacContext context)
        {
            int lps = H264CabacTables.LpsRange(context.State, Range);
            if (value != context.MostProbableSymbol)
            {
                Range = lps;
                context = context.NextLeastProbable();
            }
            else
            {
                Range -= lps;
                context = context.NextMostProbable();
            }
            Normalize();
        }

        public void WriteBypass(bool value) => bits++;

        public void WriteBypassBits(uint value, int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 32);
            if (count < 32 && value >> count != 0) { throw new ArgumentOutOfRangeException(nameof(value)); }
            bits += count;
        }

        public void WriteTermination(bool value)
        {
            if (value) { throw new NotSupportedException("Final CABAC termination is not a macroblock cost."); }
            Range -= 2;
            Normalize();
        }

        private void Normalize()
        {
            int shift = BitOperations.LeadingZeroCount((uint)Range) - 23;
            Range <<= shift;
            bits += shift;
        }
    }
}
