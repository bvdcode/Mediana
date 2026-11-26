// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mpeg2
{
    internal readonly record struct Mpeg2MotionVector(int X, int Y)
    {
        public static Mpeg2MotionVector Read(scoped ref Mpeg2BitReader bits, Mpeg2MotionVector previous, int xCode, int yCode) =>
            new(ReadComponent(ref bits, previous.X, xCode), ReadComponent(ref bits, previous.Y, yCode));

        private static int ReadComponent(scoped ref Mpeg2BitReader bits, int previous, int code)
        {
            if (code is < 1 or > 9) { throw new InvalidDataException("The MPEG-2 motion vector range is invalid."); }
            int motion = Mpeg2Tables.Motion.Read(ref bits);
            if (motion == 0) { return previous; }
            int residualBits = code - 1;
            int delta = ((Math.Abs(motion) - 1) << residualBits) + bits.Read(residualBits) + 1;
            if (motion < 0) { delta = -delta; }
            int bound = 16 << residualBits;
            return ((previous + delta + bound) & (2 * bound - 1)) - bound;
        }
    }
}
