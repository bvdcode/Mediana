using System.Numerics;

namespace Mediana.Codecs.Mp3
{
    internal static class Mp3Huffman
    {
        public static (int X, int Y) ReadPair(ref Mp3BitReader bits, int table)
        {
            if (table == 0)
            {
                return (0, 0);
            }
            int extension = table switch
            {
                1 or 2 or 3 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 15 => 0,
                16 => 1,
                17 => 2,
                18 => 3,
                19 or 24 => 4,
                25 => 5,
                20 or 26 => 6,
                27 => 7,
                21 or 28 => 8,
                29 => 9,
                22 => 10,
                30 => 11,
                23 or 31 => 13,
                _ => throw new InvalidDataException("The MP3 pair codebook is reserved or invalid.")
            };
            if (!TryReadSymbol(ref bits, Mp3HuffmanTables.Get(table), out int symbol))
            {
                throw new InvalidDataException("The MP3 Huffman pair exceeds its granule boundary.");
            }
            int x = ReadSignedValue(ref bits, symbol >> 4, extension);
            int y = ReadSignedValue(ref bits, symbol & 15, extension);
            return (x, y);
        }

        /// <summary>An incomplete final quadruple is stuffing. Failed reads leave the cursor and output unchanged.</summary>
        public static bool TryReadQuad(ref Mp3BitReader bits, int table, Span<int> values)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(values.Length, 4);
            int codebook = table switch
            {
                0 => 32,
                1 => 33,
                _ => throw new InvalidDataException("The MP3 quadruple codebook is invalid.")
            };
            Mp3BitReader candidate = bits;
            if (!TryReadSymbol(ref candidate, Mp3HuffmanTables.Get(codebook), out int symbol)
                || BitOperations.PopCount((uint)symbol) > candidate.Remaining)
            {
                return false;
            }
            for (int index = 0; index < 4; index++)
            {
                int value = (symbol >> (3 - index)) & 1;
                values[index] = value == 0 ? 0 : (candidate.Read(1) == 0 ? 1 : -1);
            }
            bits = candidate;
            return true;
        }

        private static int ReadSignedValue(ref Mp3BitReader bits, int value, int extension)
        {
            if (value == 15 && extension != 0)
            {
                value += bits.Read(extension);
            }
            return value == 0 || bits.Read(1) == 0 ? value : -value;
        }

        private static bool TryReadSymbol(ref Mp3BitReader bits, ReadOnlySpan<short> tree, out int symbol)
        {
            int node = 0;
            while (node >= 0)
            {
                if (bits.Remaining == 0)
                {
                    symbol = 0;
                    return false;
                }
                node = tree[node * 2 + bits.Read(1)];
            }
            symbol = -node - 1;
            return true;
        }
    }
}
