// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;

namespace Mediana.Images.Heif.Hevc
{
    /// <summary>
    /// Stores one worker-owned CABAC probability state and its most probable symbol.
    /// </summary>
    /// <remarks>
    /// The low bit holds the symbol; the next six bits hold the probability state.
    /// Only validated states and their normative transitions can construct a packed value.
    /// </remarks>
    internal readonly struct CabacContext
    {
        private const int StateCount = 64;
        private const int LastAdaptiveState = 62;
        private static readonly CabacContext[] LeastProbableTransitions = CreateLeastProbableTransitions();
        private readonly byte packed;

        public CabacContext(byte state, bool mostProbableSymbol)
        {
            if (state >= StateCount)
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }
            packed = (byte)((state << 1) | (mostProbableSymbol ? 1 : 0));
        }

        private CabacContext(byte packed)
        {
            this.packed = packed;
        }

        public byte State => (byte)(packed >> 1);
        public bool MostProbableSymbol => (packed & 1) != 0;

        public CabacContext NextMostProbable() => new CabacContext((byte)(packed < (LastAdaptiveState * 2) ? packed + 2 : packed));

        public CabacContext NextLeastProbable() => LeastProbableTransitions[packed];

        public static CabacContext Initialize(byte initializationValue, int sliceQp)
        {
            int slope = ((initializationValue >> 4) * 5) - 45;
            int intercept = ((initializationValue & 15) * 8) - 16;
            int preState = Math.Max(1, Math.Min(126, ((slope * Math.Max(0, Math.Min(51, sliceQp))) >> 4) + intercept));
            return new CabacContext((byte)(preState <= 63 ? 63 - preState : preState - 64), preState > 63);
        }

        private static CabacContext[] CreateLeastProbableTransitions()
        {
            CabacContext[] transitions = new CabacContext[StateCount * 2];
            for (int state = 0; state < StateCount; state++)
            {
                for (int symbol = 0; symbol < 2; symbol++)
                {
                    bool nextSymbol = (symbol != 0) != (state == 0);
                    transitions[(state * 2) + symbol] = new CabacContext(CabacTables.NextLps(state), nextSymbol);
                }
            }
            return transitions;
        }
    }
}
