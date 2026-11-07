// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Opus
{
    internal class SilkStereoState
    {
        public short MidPrevious2 { get; set; }
        public short MidPrevious1 { get; set; }
        public short SidePrevious2 { get; set; }
        public short SidePrevious1 { get; set; }
        public SilkStereoPrediction Prediction { get; set; }

        public void Reset()
        {
            MidPrevious2 = MidPrevious1 = SidePrevious2 = SidePrevious1 = 0;
            Prediction = default;
        }
    }
}
