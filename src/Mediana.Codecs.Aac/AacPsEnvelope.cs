// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal class AacPsEnvelope
    {
        public int EndSlot { get; set; }
        public int[] Intensity { get; } = new int[34];
        public int[] Coherence { get; } = new int[34];
        public int[] PhaseDifference { get; } = new int[17];
        public int[] OverallPhase { get; } = new int[17];

        public void Clear()
        {
            EndSlot = 0;
            Array.Clear(Intensity);
            Array.Clear(Coherence);
            Array.Clear(PhaseDifference);
            Array.Clear(OverallPhase);
        }

        public void CopyFrom(AacPsEnvelope source)
        {
            EndSlot = source.EndSlot;
            source.Intensity.CopyTo(Intensity, 0);
            source.Coherence.CopyTo(Coherence, 0);
            source.PhaseDifference.CopyTo(PhaseDifference, 0);
            source.OverallPhase.CopyTo(OverallPhase, 0);
        }
    }
}
