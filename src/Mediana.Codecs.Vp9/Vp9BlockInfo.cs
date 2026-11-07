// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal struct Vp9BlockInfo
    {
        internal uint SubModes;
        internal Vp9MotionSet Motion;
        internal byte ReferenceZero, ReferenceOne, Interpolation;
        internal byte Size, Transform, Segment, Mode;
        internal bool Skip;
        internal readonly bool Inter => ReferenceZero > 0;
        internal readonly int Reference(int list) => list == 0 ? ReferenceZero : ReferenceOne;
        internal readonly Vp9MotionVector Vector(int list, int sub) => Motion[list * 4 + sub];
        internal readonly int SubMode(int index) => (int)(SubModes >> (index * 8)) & 255;
    }
}
