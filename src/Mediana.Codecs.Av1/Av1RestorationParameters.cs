// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1RestorationParameters(Av1RestorationType Y, Av1RestorationType U, Av1RestorationType V, int LumaSize, int ChromaSize)
    {
        internal bool Enabled => Y != Av1RestorationType.None || U != Av1RestorationType.None || V != Av1RestorationType.None;
        internal Av1RestorationType Type(int plane) => plane switch
        {
            0 => Y,
            1 => U,
            2 => V,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };
        internal int Size(int plane) => plane switch
        {
            0 => LumaSize,
            1 or 2 => ChromaSize,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };
        internal int Rows(Av1SequenceHeader sequence, int plane) => Count(Size(plane), (sequence.Height + sequence.PlaneShift(plane)) >> sequence.PlaneShift(plane));
        internal int Columns(Av1SequenceHeader sequence, int plane) => Count(Size(plane), (sequence.Width + sequence.PlaneShift(plane)) >> sequence.PlaneShift(plane));
        internal int PlaneUnits(Av1SequenceHeader sequence, int plane) => Type(plane) == Av1RestorationType.None ? 0 : checked(Rows(sequence, plane) * Columns(sequence, plane));
        internal int Offset(Av1SequenceHeader sequence, int plane) => plane switch
        {
            0 => 0,
            1 => PlaneUnits(sequence, 0),
            2 => checked(PlaneUnits(sequence, 0) + PlaneUnits(sequence, 1)),
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };
        internal int UnitCount(Av1SequenceHeader sequence) => checked(PlaneUnits(sequence, 0) + (sequence.Monochrome ? 0 : PlaneUnits(sequence, 1) + PlaneUnits(sequence, 2)));
        private static int Count(int size, int extent) => Math.Max((extent + size / 2) / size, 1);

        internal static Av1RestorationParameters Read(ref Av1BitReader bits, Av1SequenceHeader sequence)
        {
            Av1RestorationType y = ReadType(ref bits);
            Av1RestorationType u = Av1RestorationType.None;
            Av1RestorationType v = Av1RestorationType.None;
            if (!sequence.Monochrome) { u = ReadType(ref bits); v = ReadType(ref bits); }
            int size = 0;
            int uvSize = 0;
            if (y != Av1RestorationType.None || u != Av1RestorationType.None || v != Av1RestorationType.None)
            {
                int shift = bits.Read(1);
                if (sequence.Superblock128) { shift++; }
                else if (shift != 0) { shift += bits.Read(1); }
                size = 64 << shift;
                uvSize = size;
                if (!sequence.Monochrome && (u != Av1RestorationType.None || v != Av1RestorationType.None)) { uvSize >>= bits.Read(1); }
            }
            return new(y, u, v, size, uvSize);
        }

        private static Av1RestorationType ReadType(ref Av1BitReader bits) => bits.Read(2) switch
        {
            0 => Av1RestorationType.None,
            1 => Av1RestorationType.Switchable,
            2 => Av1RestorationType.Wiener,
            3 => Av1RestorationType.SelfGuided,
            _ => throw new InvalidDataException("The AV1 restoration type is invalid.")
        };
    }
}
