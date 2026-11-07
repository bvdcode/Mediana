// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;
using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal class VorbisCodebook : IDisposable
    {
        private const int PrefixBits = 8;
        private const int PrefixEntries = 1 << PrefixBits;
        private readonly WritableMediaBuffer storage;
        private readonly int lookupType, valuesOffset, valueCount;
        private readonly float minimum, delta;
        private readonly bool sequence;

        public VorbisCodebook(int dimensions, ReadOnlySpan<byte> lengths, int lookupType, uint minimum,
            uint delta, bool sequence, ReadOnlySpan<ushort> values, MediaBufferAllocator allocator, CancellationToken token)
        {
            Dimensions = dimensions;
            Entries = lengths.Length;
            this.lookupType = lookupType;
            this.minimum = Unpack(minimum);
            this.delta = Unpack(delta);
            this.sequence = sequence;
            valueCount = values.Length;
            int used = 0;
            foreach (byte length in lengths) { if (length != 0) { used++; } }
            valuesOffset = checked((PrefixEntries + 2 * (used + 33)) * sizeof(int));
            storage = allocator.Allocate(checked(valuesOffset + values.Length * sizeof(ushort)));
            bool success = false;
            try
            {
                Build(lengths, used, token);
                values.CopyTo(MemoryMarshal.Cast<byte, ushort>(storage.Span[valuesOffset..]));
                success = true;
            }
            finally { if (!success) { storage.Dispose(); } }
        }

        public int Dimensions { get; }
        public int Entries { get; }
        public bool HasVectors => lookupType != 0;

        public bool TryDecode(ref VorbisBitReader reader, out int entry)
        {
            ReadOnlySpan<int> all = MemoryMarshal.Cast<byte, int>(storage.Span[..valuesOffset]);
            ReadOnlySpan<int> tree = all[PrefixEntries..];
            int node = 1;
            if (reader.TryPeek(PrefixBits, out uint prefix))
            {
                int hint = all[(int)prefix];
                if (hint < 0)
                {
                    int packed = ~hint;
                    reader.Skip(packed & 63);
                    entry = packed >> 6;
                    return true;
                }
                if (hint == 0) { throw new InvalidDataException("The Vorbis codeword is absent from its codebook."); }
                reader.Skip(PrefixBits);
                node = hint;
            }
            while (reader.Remaining > 0)
            {
                int child = tree[2 * node + (int)reader.Read(1)];
                if (child < 0) { entry = ~child; return true; }
                if (child == 0) { throw new InvalidDataException("The Vorbis codeword is absent from its codebook."); }
                node = child;
            }
            entry = 0;
            return false;
        }

        public void ReadVector(int entry, Span<float> output)
        {
            if (!HasVectors || entry < 0 || entry >= Entries || output.Length < Dimensions)
            {
                throw new InvalidDataException("The Vorbis vector lookup is absent or outside its codebook.");
            }
            ReadOnlySpan<ushort> values = MemoryMarshal.Cast<byte, ushort>(storage.Span[valuesOffset..]);
            int quotient = entry;
            float last = 0;
            for (int dimension = 0; dimension < Dimensions; dimension++)
            {
                int index = lookupType switch
                {
                    1 => quotient % valueCount,
                    2 => checked(entry * Dimensions + dimension),
                    _ => throw new InvalidOperationException("The Vorbis vector lookup type is invalid.")
                };
                float value = (float)((double)values[index] * delta + minimum + last);
                output[dimension] = value;
                if (sequence) { last = value; }
                if (lookupType == 1) { quotient /= valueCount; }
            }
        }

        private void Build(ReadOnlySpan<byte> lengths, int used, CancellationToken token)
        {
            Span<int> all = MemoryMarshal.Cast<byte, int>(storage.Span[..valuesOffset]);
            Span<int> tree = all[PrefixEntries..];
            Span<uint> markers = stackalloc uint[33];
            markers.Clear();
            int nextNode = 2;
            for (int entry = 0; entry < lengths.Length; entry++)
            {
                if ((entry & 4095) == 0) { token.ThrowIfCancellationRequested(); }
                int length = lengths[entry];
                if (length == 0) { continue; }
                if (length > 32) { throw new InvalidDataException("A Vorbis codeword exceeds 32 bits."); }
                uint code = markers[length];
                if (length < 32 && code >> length != 0) { throw new InvalidDataException("A Vorbis codebook is oversubscribed."); }
                int node = 1;
                for (int bit = length - 1; bit >= 0; bit--)
                {
                    int at = 2 * node + (int)((code >> bit) & 1);
                    if (tree[at] < 0) { throw new InvalidDataException("Vorbis codewords overlap."); }
                    if (bit == 0)
                    {
                        if (tree[at] != 0) { throw new InvalidDataException("Vorbis codewords overlap."); }
                        tree[at] = ~entry;
                    }
                    else
                    {
                        if (tree[at] == 0)
                        {
                            if (nextNode >= tree.Length / 2) { throw new InvalidDataException("A Vorbis codebook does not form a complete prefix tree."); }
                            tree[at] = nextNode++;
                        }
                        node = tree[at];
                    }
                }
                AdvanceMarkers(markers, length, code);
            }
            if (used != 0 && !(used == 1 && markers[2] == 2))
            {
                for (int length = 1; length <= 32; length++)
                {
                    if ((markers[length] & (uint.MaxValue >> (32 - length))) != 0)
                    {
                        throw new InvalidDataException("A Vorbis codebook does not form a complete prefix tree.");
                    }
                }
            }
            for (int prefix = 0; prefix < PrefixEntries; prefix++)
            {
                int node = 1, consumed = 0;
                while (node > 0 && consumed < PrefixBits) { node = tree[2 * node + ((prefix >> consumed++) & 1)]; }
                all[prefix] = node < 0 ? ~((~node << 6) | consumed) : node;
            }
        }

        private static void AdvanceMarkers(Span<uint> markers, int length, uint code)
        {
            for (int depth = length; depth > 0; depth--)
            {
                if ((markers[depth] & 1) != 0)
                {
                    markers[depth] = depth == 1 ? unchecked(markers[depth] + 1) : markers[depth - 1] << 1;
                    break;
                }
                markers[depth]++;
            }
            for (int depth = length + 1; depth <= 32; depth++)
            {
                if (markers[depth] >> 1 != code) { break; }
                code = markers[depth];
                markers[depth] = markers[depth - 1] << 1;
            }
        }

        private static float Unpack(uint value)
        {
            int mantissa = (int)(value & 0x1fffff);
            if ((value & 0x80000000) != 0) { mantissa = -mantissa; }
            int exponent = (int)((value >> 21) & 1023) - 788;
            return MathF.ScaleB(mantissa, Math.Clamp(exponent, -63, 63));
        }

        public void Dispose() => storage.Dispose();
    }
}
