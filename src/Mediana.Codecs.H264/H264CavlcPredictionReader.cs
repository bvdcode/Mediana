// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal readonly struct H264CavlcPredictionReader : IH264PredictionReader
    {
        public int ReadSubType(ref H264BitReader bits, H264SliceKind kind) => kind switch
        {
            H264SliceKind.P => bits.ReadUe(3),
            H264SliceKind.B => bits.ReadUe(12),
            H264SliceKind.I or H264SliceKind.Sp or H264SliceKind.Si => throw new InvalidDataException("The slice has no supported inter sub-macroblock syntax."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public int ReadReference(ref H264BitReader bits, H264ReferenceList list, int x, int y, int width, int height, int maximum) =>
            maximum == 1 ? 1 - (int)bits.ReadBits(1) : bits.ReadUe(maximum);

        public (int X, int Y) ReadMotionDifference(ref H264BitReader bits, H264ReferenceList list, int x, int y, int width, int height) =>
            (bits.ReadSe(short.MinValue, short.MaxValue), bits.ReadSe(short.MinValue, short.MaxValue));
    }
}
