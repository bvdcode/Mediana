// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Aac
{
    internal struct AacWindowSelector
    {
        private int sequence;
        private bool started;
        private bool previousTransient;
        private bool expectedTransient;

        public int Select(bool currentTransient, bool nextTransient)
        {
            if (started && expectedTransient != currentTransient)
            {
                throw new InvalidOperationException("AAC window lookahead must describe the same next PCM block.");
            }
            bool currentShort = previousTransient || currentTransient;
            bool nextShort = currentTransient || nextTransient;
            if (!started)
            {
                if (currentShort)
                {
                    sequence = 2;
                }
                else
                {
                    sequence = nextShort ? 1 : 0;
                }
            }
            else
            {
                sequence = sequence switch
                {
                    0 or 3 => nextShort ? 1 : 0,
                    1 => 2,
                    2 => currentShort || nextShort ? 2 : 3,
                    _ => throw new InvalidOperationException("The preceding AAC window sequence is invalid.")
                };
            }
            previousTransient = currentTransient;
            expectedTransient = nextTransient;
            started = true;
            return sequence;
        }
    }
}
