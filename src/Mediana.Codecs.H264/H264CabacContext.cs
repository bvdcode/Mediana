// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    /// <summary>Stores one CABAC probability state and its most probable symbol.</summary>
    internal readonly struct H264CabacContext
    {
        private const int StateCount = 64;
        private const int LastAdaptiveState = 62;
        private static readonly H264CabacContext[] LeastProbableTransitions = CreateLeastProbableTransitions();
        private readonly byte packed;

        public H264CabacContext(byte state, bool mostProbableSymbol)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(state, StateCount);
            packed = (byte)((state << 1) | (mostProbableSymbol ? 1 : 0));
        }

        private H264CabacContext(byte packed)
        {
            this.packed = packed;
        }

        public byte State => (byte)(packed >> 1);
        public bool MostProbableSymbol => (packed & 1) != 0;

        public H264CabacContext NextMostProbable() =>
            new((byte)(packed < LastAdaptiveState * 2 ? packed + 2 : packed));

        public H264CabacContext NextLeastProbable() => LeastProbableTransitions[packed];

        /// <summary>Initializes H.264 context variables from the table's signed m/n pair and SliceQPY.</summary>
        public static H264CabacContext Initialize(sbyte slope, sbyte intercept, int sliceQp)
        {
            int preState = Math.Clamp(((slope * Math.Clamp(sliceQp, 0, 51)) >> 4) + intercept, 1, 126);
            return new H264CabacContext((byte)(preState <= 63 ? 63 - preState : preState - 64), preState > 63);
        }

        private static H264CabacContext[] CreateLeastProbableTransitions()
        {
            H264CabacContext[] transitions = new H264CabacContext[StateCount * 2];
            for (int state = 0; state < StateCount; state++)
            {
                for (int symbol = 0; symbol < 2; symbol++)
                {
                    bool nextSymbol = (symbol != 0) != (state == 0);
                    transitions[state * 2 + symbol] = new H264CabacContext(H264CabacTables.NextLps(state), nextSymbol);
                }
            }
            return transitions;
        }
    }
}
