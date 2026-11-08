// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal struct Av1FilterBlock
    {
        internal byte BlockSize;
        internal byte LumaTransform;
        internal byte ChromaTransform;
        private byte prediction;
        internal Av1BlockFilterDeltas Deltas;

        internal int Reference => prediction & 7;
        internal bool MotionMode => (prediction & 8) != 0;
        internal bool Skip => (prediction & 16) != 0;
        internal void SetPrediction(int reference, bool motionMode, bool skip) =>
            prediction = (byte)(reference | (motionMode ? 8 : 0) | (skip ? 16 : 0));
        internal int Transform(int plane) => plane switch
        {
            0 => LumaTransform,
            1 or 2 => ChromaTransform,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };
        internal int Width4
        {
            get
            {
                ReadOnlySpan<byte> values = [1, 1, 2, 2, 2, 4, 4, 4, 8, 8, 8, 16, 16, 16, 32, 32, 1, 4, 2, 8, 4, 16];
                return values[BlockSize];
            }
        }
        internal int Height4
        {
            get
            {
                ReadOnlySpan<byte> values = [1, 2, 1, 2, 4, 2, 4, 8, 4, 8, 16, 8, 16, 32, 16, 32, 4, 1, 8, 2, 16, 4];
                return values[BlockSize];
            }
        }
    }
}
