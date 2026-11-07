// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;

namespace Mediana.Codecs.Theora
{
    internal class TheoraSetup
    {
        private readonly ushort[] quantizers = new ushort[6 * 64 * 64];
        internal byte[] FilterLimits { get; } = new byte[64];
        internal TheoraHuffmanTable[] Huffman { get; } = new TheoraHuffmanTable[80];
        internal ReadOnlySpan<ushort> Quantizer(int type, int plane, int quality) => quantizers.AsSpan(((type * 3 + plane) * 64 + quality) * 64, 64);

        internal static TheoraSetup Read(ReadOnlySpan<byte> packet, CancellationToken cancellationToken = default)
        {
            if (packet.Length < 7 || packet[0] != 0x82 || !packet.Slice(1, 6).SequenceEqual("theora"u8))
            { throw new InvalidDataException("The Theora setup header is absent."); }
            TheoraBitReader reader = new(packet[7..]); TheoraSetup result = new();
            int bits = reader.Read(3);
            for (int i = 0; i < 64; i++) { result.FilterLimits[i] = (byte)reader.Read(bits); }
            int[] ac = new int[64], dc = new int[64];
            bits = reader.Read(4) + 1;
            for (int i = 0; i < 64; i++) { ac[i] = reader.Read(bits); }
            bits = reader.Read(4) + 1;
            for (int i = 0; i < 64; i++) { dc[i] = reader.Read(bits); }
            int count = reader.Read(9) + 1;
            if (count > 384) { throw new InvalidDataException("The Theora setup has too many base matrices."); }
            byte[] bases = new byte[count * 64];
            for (int i = 0; i < bases.Length; i++) { bases[i] = (byte)reader.Read(8); }
            TheoraQuantRange[] ranges = new TheoraQuantRange[6];
            for (int i = 0; i < 6; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i > 0 && !reader.Flag())
                { ranges[i] = ranges[i >= 3 && reader.Flag() ? i - 3 : i - 1]; }
                else { ranges[i] = ReadRange(ref reader, count); }
                result.BuildQuantizers(i, ranges[i], bases, ac, dc);
            }
            for (int i = 0; i < 80; i++)
            { cancellationToken.ThrowIfCancellationRequested(); result.Huffman[i] = TheoraHuffmanTable.Read(ref reader); }
            return result;
        }

        private static TheoraQuantRange ReadRange(ref TheoraBitReader reader, int count)
        {
            List<int> sizes = [], matrices = [];
            int indexBits = BitOperations.Log2((uint)(count - 1)) + 1;
            if (count == 1) { indexBits = 0; }
            int quality = 0, matrix = reader.Read(indexBits);
            if (matrix >= count) { throw new InvalidDataException("A Theora base matrix index is out of range."); }
            matrices.Add(matrix);
            do
            {
                int size = reader.Read(quality == 62 ? 0 : BitOperations.Log2((uint)(62 - quality)) + 1) + 1;
                quality += size;
                if (quality > 63) { throw new InvalidDataException("The Theora quantization ranges exceed 63."); }
                sizes.Add(size); matrix = reader.Read(indexBits);
                if (matrix >= count) { throw new InvalidDataException("A Theora base matrix index is out of range."); }
                matrices.Add(matrix);
            } while (quality < 63);
            return new(sizes.ToArray(), matrices.ToArray());
        }

        private void BuildQuantizers(int index, TheoraQuantRange range, ReadOnlySpan<byte> bases, int[] ac, int[] dc)
        {
            int start = 0;
            for (int r = 0; r < range.Sizes.Length; r++)
            {
                int size = range.Sizes[r], end = start + size;
                ReadOnlySpan<byte> first = bases.Slice(range.Matrices[r] * 64, 64), last = bases.Slice(range.Matrices[r + 1] * 64, 64);
                for (int quality = start; quality <= end; quality++)
                {
                    Span<ushort> target = quantizers.AsSpan((index * 64 + quality) * 64, 64);
                    for (int coefficient = 0; coefficient < 64; coefficient++)
                    {
                        int basis = (2 * (end - quality) * first[coefficient] + 2 * (quality - start) * last[coefficient] + size) / (2 * size);
                        int minimum = (coefficient == 0 ? 16 : 8) << (index / 3);
                        int scale = coefficient == 0 ? dc[quality] : ac[quality];
                        target[coefficient] = (ushort)Math.Clamp(scale * basis / 100 * 4, minimum, 4096);
                    }
                }
                start = end;
            }
        }
    }
}
