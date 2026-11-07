// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Hevc
{
    internal readonly record struct HevcMotionSample(int PictureOrder0, int PictureOrder1,
        HevcMotionVector Vector0, HevcMotionVector Vector1, sbyte Reference0, sbyte Reference1, byte Flags)
    {
        public const byte List0 = 1, List1 = 2, LongTerm0 = 4, LongTerm1 = 8, Available = 16;
        public bool IsInter => (Flags & (List0 | List1)) != 0;
        public bool IsAvailable => (Flags & Available) != 0;

        public (int Order, int Index, HevcMotionVector Vector, bool LongTerm) Direction(int list) => list switch
        {
            0 => (PictureOrder0, Reference0, Vector0, (Flags & LongTerm0) != 0),
            1 => (PictureOrder1, Reference1, Vector1, (Flags & LongTerm1) != 0),
            _ => throw new ArgumentOutOfRangeException(nameof(list))
        };

        public bool Uses(int list) => list switch
        {
            0 => (Flags & List0) != 0,
            1 => (Flags & List1) != 0,
            _ => throw new ArgumentOutOfRangeException(nameof(list))
        };

        public bool SamePrediction(HevcMotionSample other) =>
            (Flags & (List0 | List1)) == (other.Flags & (List0 | List1)) &&
            (!Uses(0) || Reference0 == other.Reference0 && Vector0 == other.Vector0) &&
            (!Uses(1) || Reference1 == other.Reference1 && Vector1 == other.Vector1);
    }
}
