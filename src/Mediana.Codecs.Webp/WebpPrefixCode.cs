// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal class WebpPrefixCode
    {
        private readonly Memory<byte> storage;
        private readonly int only = -1;
        private static ReadOnlySpan<int> LengthOrder => [17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        private WebpPrefixCode(ReadOnlySpan<byte> lengths, MediaBufferWorkspace workspace)
        {
            storage = workspace.Allocate((48 + 256 + lengths.Length) * sizeof(int));
            Span<int> data = MemoryMarshal.Cast<byte, int>(storage.Span);
            Span<int> counts = data[..16], first = data.Slice(16, 16), indexes = data.Slice(32, 16), fast = data.Slice(48, 256), symbols = data[304..];
            int used = 0;
            for (int symbol = 0; symbol < lengths.Length; symbol++)
            {
                int length = lengths[symbol];
                if (length != 0) { counts[length]++; used++; only = symbol; }
            }
            if (used == 1)
            {
                if (lengths[only] != 1) { throw new InvalidDataException("Invalid VP8L single-symbol prefix code."); }
                return;
            }
            only = -1;
            int remaining = 1, code = 0, offset = 0;
            Span<int> next = stackalloc int[16]; next.Clear();
            for (int length = 1; length <= 15; length++)
            {
                remaining = (remaining << 1) - counts[length];
                if (remaining < 0) { throw new InvalidDataException("Oversubscribed VP8L prefix tree."); }
                code = (code + counts[length - 1]) << 1;
                first[length] = code; indexes[length] = offset; next[length] = offset; offset += counts[length];
            }
            if (remaining != 0) { throw new InvalidDataException("Incomplete VP8L prefix tree."); }
            for (int symbol = 0; symbol < lengths.Length; symbol++)
            {
                int length = lengths[symbol];
                if (length == 0) { continue; }
                int index = next[length]++; symbols[index] = symbol;
                if (length <= 8)
                {
                    int canonical = first[length] + index - indexes[length], reversed = 0;
                    for (int bit = 0; bit < length; bit++) { reversed = (reversed << 1) | ((canonical >> bit) & 1); }
                    for (int fill = reversed; fill < 256; fill += 1 << length) { fast[fill] = (symbol << 5) | length; }
                }
            }
        }
        internal int Decode(ref WebpBitReader input)
        {
            if (only >= 0) { return only; }
            ReadOnlySpan<int> data = MemoryMarshal.Cast<byte, int>(storage.Span);
            int quick = data[48 + (int)input.Peek(8)];
            if (quick != 0) { input.Skip(quick & 31); return quick >> 5; }
            int code = 0;
            for (int bits = 1; bits <= 15; bits++)
            {
                code = (code << 1) | input.Read(1);
                int index = code - data[16 + bits];
                if ((uint)index < (uint)data[bits]) { return data[304 + data[32 + bits] + index]; }
            }
            throw new InvalidDataException("Invalid VP8L prefix symbol.");
        }
        internal static WebpPrefixCode Read(ref WebpBitReader input, int alphabet, MediaBufferWorkspace workspace)
        {
            Span<byte> lengths = stackalloc byte[alphabet]; lengths.Clear();
            if (input.Read(1) != 0)
            {
                int count = input.Read(1) + 1, bits = 1 + input.Read(1) * 7;
                int first = input.Read(bits);
                if (first >= alphabet) { throw new InvalidDataException("VP8L prefix symbol exceeds its alphabet."); }
                lengths[first] = 1;
                if (count == 2)
                {
                    int second = input.Read(8);
                    if (second >= alphabet) { throw new InvalidDataException("VP8L prefix symbol exceeds its alphabet."); }
                    lengths[second] = 1;
                }
            }
            else
            {
                Span<byte> codeLengths = stackalloc byte[19]; codeLengths.Clear();
                int count = input.Read(4) + 4;
                for (int index = 0; index < count; index++) { codeLengths[LengthOrder[index]] = (byte)input.Read(3); }
                WebpPrefixCode tree = new(codeLengths, workspace);
                int maximum = alphabet;
                if (input.Read(1) != 0) { int bits = 2 + input.Read(3) * 2; maximum = 2 + input.Read(bits); }
                if (maximum > alphabet) { throw new InvalidDataException("VP8L prefix length count exceeds its alphabet."); }
                int position = 0; byte previous = 8;
                while (position < alphabet && maximum-- > 0)
                {
                    int symbol = tree.Decode(ref input);
                    if (symbol <= 15)
                    {
                        lengths[position++] = (byte)symbol;
                        if (symbol != 0) { previous = (byte)symbol; }
                    }
                    else
                    {
                        int repeat = symbol switch
                        {
                            16 => input.Read(2) + 3,
                            17 => input.Read(3) + 3,
                            18 => input.Read(7) + 11,
                            _ => throw new InvalidDataException("Invalid VP8L code length repeat.")
                        };
                        if (repeat > alphabet - position) { throw new InvalidDataException("VP8L code length repeat exceeds its alphabet."); }
                        lengths.Slice(position, repeat).Fill(symbol == 16 ? previous : (byte)0); position += repeat;
                    }
                }
            }
            return new(lengths, workspace);
        }
    }
}
