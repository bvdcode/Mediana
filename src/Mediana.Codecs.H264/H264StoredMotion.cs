// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.H264
{
    internal readonly ref struct H264StoredMotion(ReadOnlySpan<byte> bytes, int count, bool wide)
    {
        private readonly ReadOnlySpan<uint> pairs = MemoryMarshal.Cast<byte, uint>(bytes);
        private readonly bool storesWide = wide;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (int X, int Y) Read(int index)
        {
            if (storesWide)
            {
                return ((int)pairs[index], (int)pairs[count + index]);
            }
                        uint pair = pairs[index];
            return ((short)pair, (short)(pair >> 16));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Near(H264StoredMotion other, int previous, int current, int verticalLimit)
        {
            if (!storesWide)
            {
                uint p = pairs[previous];
                uint q = other.pairs[current];
                int dx = (short)p - (short)q;
                int dy = (short)(p >> 16) - (short)(q >> 16);
                return Math.Abs(dx) < 4 && Math.Abs(dy) < verticalLimit;
            }
            (int px, int py) = Read(previous);
            (int qx, int qy) = other.Read(current);
            return Math.Abs((long)px - qx) < 4 && Math.Abs((long)py - qy) < verticalLimit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsUniformRow(int first, int start)
        {
            if (storesWide)
            {
                return Vector128.EqualsAll(Vector128.Create(pairs.Slice(start, 4)), Vector128.Create(pairs[first]))
                    && Vector128.EqualsAll(Vector128.Create(pairs.Slice(count + start, 4)), Vector128.Create(pairs[count + first]));
            }
                        return Vector128.EqualsAll(Vector128.Create(pairs.Slice(start, 4)), Vector128.Create(pairs[first]));
        }
    }
}
