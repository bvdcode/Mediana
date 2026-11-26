// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Pcm
{
    internal struct ImaAdpcmPredictor(int sample, int index)
    {
        private static ReadOnlySpan<short> Steps =>
        [
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
            34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
            157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
            724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
            3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        ];
        private static ReadOnlySpan<sbyte> IndexDeltas4 => [-1, -1, -1, -1, 2, 4, 6, 8];
        private static ReadOnlySpan<sbyte> IndexDeltas3 => [-1, -1, 1, 2];

        public const int MaximumIndex = 88;
        public int Sample { get; private set; } = sample;
        public int Index { get; private set; } = index;

        public short Decode4(int code) => Expand(code, 8, IndexDeltas4);

        public short Decode3(int code) => Expand(code, 4, IndexDeltas3);

        private short Expand(int code, int signBit, ReadOnlySpan<sbyte> indexDeltas)
        {
            int step = Steps[Index];
            int magnitudeCode = code & (signBit - 1);
            int difference = step >> (signBit == 8 ? 3 : 2);
            if ((magnitudeCode & (signBit >> 1)) != 0)
            {
                difference += step;
            }
            if ((magnitudeCode & (signBit >> 2)) != 0)
            {
                difference += step >> 1;
            }
            if (signBit == 8 && (magnitudeCode & 1) != 0)
            {
                difference += step >> 2;
            }
            Sample = Math.Clamp(Sample + ((code & signBit) == 0 ? difference : -difference), short.MinValue, short.MaxValue);
            Index = Math.Clamp(Index + indexDeltas[magnitudeCode], 0, Steps.Length - 1);
            return (short)Sample;
        }
    }
}
