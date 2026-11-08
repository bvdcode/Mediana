// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1InterPrecision<T> where T : unmanaged, IBinaryInteger<T>
    {
        private const int FilterBits = 7;
        private const int ByteHorizontalShift = 3;
        private const int BytePostShift = FilterBits - ByteHorizontalShift;
        private readonly int horizontalShift;
        private readonly int maximum;
        private readonly int compoundOffset;
        private readonly int differenceShift;

        internal Av1InterPrecision(int bitDepth)
        {
            if (typeof(T) == typeof(byte))
            {
                ArgumentOutOfRangeException.ThrowIfNotEqual(bitDepth, 8);
                bitDepth = 8;
            }
            int shift = bitDepth switch
            {
                8 or 10 => ByteHorizontalShift,
                12 => 5,
                _ => throw new ArgumentOutOfRangeException(nameof(bitDepth))
            };
            horizontalShift = shift;
            maximum = (1 << bitDepth) - 1;
            int postShift = FilterBits - shift;
            compoundOffset = (1 << (bitDepth + postShift)) + (1 << (bitDepth + postShift - 1));
            differenceShift = bitDepth - 8 + postShift;
        }

        internal int HorizontalShift
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => typeof(T) == typeof(byte) ? ByteHorizontalShift : horizontalShift;
        }
        internal int VerticalShift
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => typeof(T) == typeof(byte) ? 2 * FilterBits - ByteHorizontalShift : 2 * FilterBits - horizontalShift;
        }
        internal int PostShift
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => typeof(T) == typeof(byte) ? BytePostShift : FilterBits - horizontalShift;
        }
        internal int Maximum
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => typeof(T) == typeof(byte) ? byte.MaxValue : maximum;
        }
        internal int DifferenceShift
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => typeof(T) == typeof(byte) ? BytePostShift : differenceShift;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ushort StoreCompound(int value)
        {
            if (typeof(T) == typeof(byte)) { return unchecked((ushort)checked((short)value)); }
            return checked((ushort)(value + compoundOffset));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int LoadCompound(ushort value)
        {
            if (typeof(T) == typeof(byte)) { return unchecked((short)value); }
            return value - compoundOffset;
        }
    }
}
