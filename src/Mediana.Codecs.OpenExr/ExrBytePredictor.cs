// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Mediana.Codecs.OpenExr
{
    internal static class ExrBytePredictor
    {
        internal static void Restore(Span<byte> bytes, CancellationToken token)
        {
            int position = 1;
            if (Vector128.IsHardwareAccelerated)
            {
                Vector128<byte> shift1 = Vector128.Create((byte)255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);
                Vector128<byte> shift2 = Vector128.Create((byte)255, 255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);
                Vector128<byte> shift4 = Vector128.Create((byte)255, 255, 255, 255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
                Vector128<byte> shift8 = Vector128.Create((byte)255, 255, 255, 255, 255, 255, 255, 255, 0, 1, 2, 3, 4, 5, 6, 7);
                ref byte start = ref MemoryMarshal.GetReference(bytes);
                for (; position <= bytes.Length - 16; position += 16)
                {
                    if ((position & 16383) == 1) { token.ThrowIfCancellationRequested(); }
                    Vector128<byte> delta = Vector128.LoadUnsafe(ref start, (nuint)position) - Vector128.Create((byte)128);
                    delta += Vector128.Shuffle(delta, shift1);
                    delta += Vector128.Shuffle(delta, shift2);
                    delta += Vector128.Shuffle(delta, shift4);
                    delta += Vector128.Shuffle(delta, shift8);
                    delta += Vector128.Create(bytes[position - 1]);
                    delta.StoreUnsafe(ref start, (nuint)position);
                }
            }
            for (; position < bytes.Length; position++)
            {
                if ((position & 16383) == 0) { token.ThrowIfCancellationRequested(); }
                bytes[position] = unchecked((byte)(bytes[position - 1] + bytes[position] - 128));
            }
        }

        internal static void Interleave(ReadOnlySpan<byte> source, Span<byte> destination, CancellationToken token)
        {
            int middle = (source.Length + 1) / 2, pairs = source.Length / 2, position = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                ref byte start = ref MemoryMarshal.GetReference(source);
                ref byte output = ref MemoryMarshal.GetReference(destination);
                for (; position <= pairs - 16; position += 16)
                {
                    if ((position & 16383) == 0) { token.ThrowIfCancellationRequested(); }
                    Vector128<byte> even = Vector128.LoadUnsafe(ref start, (nuint)position);
                    Vector128<byte> odd = Vector128.LoadUnsafe(ref start, (nuint)(middle + position));
                    Vector128<ushort> lowEven = Vector128.WidenLower(even), highEven = Vector128.WidenUpper(even);
                    Vector128<ushort> lowOdd = Vector128.WidenLower(odd), highOdd = Vector128.WidenUpper(odd);
                    Vector128<ushort> low = BitConverter.IsLittleEndian ? lowEven | (lowOdd << 8) : (lowEven << 8) | lowOdd;
                    Vector128<ushort> high = BitConverter.IsLittleEndian ? highEven | (highOdd << 8) : (highEven << 8) | highOdd;
                    low.AsByte().StoreUnsafe(ref output, (nuint)(position * 2));
                    high.AsByte().StoreUnsafe(ref output, (nuint)(position * 2 + 16));
                }
            }
            for (; position < pairs; position++)
            {
                if ((position & 16383) == 0) { token.ThrowIfCancellationRequested(); }
                destination[position * 2] = source[position]; destination[position * 2 + 1] = source[middle + position];
            }
            if ((source.Length & 1) != 0) { destination[^1] = source[middle - 1]; }
        }
    }
}
