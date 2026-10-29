// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Codecs.Mp3;

namespace Mediana.Tests
{
    internal static class Mp3HuffmanTestInput
    {
        public static void Verify(string path)
        {
            int words = 0;
            foreach (string line in File.ReadLines(path).Skip(1))
            {
                string[] fields = line.Split('\t');
                int table = int.Parse(fields[0], CultureInfo.InvariantCulture);
                int value = int.Parse(fields[1], CultureInfo.InvariantCulture);
                string code = fields[3];
                if (code.Length != int.Parse(fields[2], CultureInfo.InvariantCulture))
                {
                    throw new InvalidDataException("The MP3 reference code length differs.");
                }
                if (table >= 32)
                {
                    VerifyQuad(table - 32, value, code);
                }
                else
                {
                    int last = table switch { 16 => 23, 24 => 31, _ => table };
                    for (int selected = table; selected <= last; selected++)
                    {
                        VerifyPair(selected, value, code);
                    }
                }
                words++;
            }
            if (words != 1410)
            {
                throw new InvalidDataException("The MP3 reference codebooks are incomplete.");
            }
            for (int offset = 0; offset < 8; offset++)
            {
                Mp3BitReader bits = new([0xff], offset, 8 - offset);
                if (Mp3Huffman.ReadPair(ref bits, 0) != (0, 0) || bits.Position != offset)
                {
                    throw new InvalidDataException("MP3 zero pairs must not consume bits.");
                }
            }
        }

        private static void VerifyPair(int table, int packed, string code)
        {
            ReadOnlySpan<byte> extensions = [1, 2, 3, 4, 6, 8, 10, 13, 4, 5, 6, 7, 8, 9, 11, 13];
            int width = table >= 16 ? extensions[table - 16] : 0;
            int maximum = (1 << width) - 1;
            foreach (int extended in new[] { 0, maximum / 2, maximum })
            {
                for (int signs = 0; signs < 4; signs++)
                {
                    int[] magnitudes = [packed >> 4, packed & 15];
                    int[] expected = new int[2];
                    string word = code;
                    for (int index = 0; index < 2; index++)
                    {
                        int magnitude = magnitudes[index];
                        if (magnitude == 15 && width != 0)
                        {
                            int extension = index == 0 ? extended : maximum - extended;
                            word += Convert.ToString(extension, 2).PadLeft(width, '0');
                            magnitude += extension;
                        }
                        if (magnitude != 0)
                        {
                            int sign = (signs >> index) & 1;
                            word += sign == 0 ? "0" : "1";
                            expected[index] = sign == 0 ? magnitude : -magnitude;
                        }
                    }
                    for (int offset = 0; offset < 8; offset++)
                    {
                        byte[] data = Pack(new string('1', offset) + word + "101101");
                        Mp3BitReader bits = new(data, offset, word.Length + 6);
                        (int x, int y) = Mp3Huffman.ReadPair(ref bits, table);
                        if (x != expected[0] || y != expected[1] || bits.Position != offset + word.Length || bits.Read(6) != 45)
                        {
                            throw new InvalidDataException($"The MP3 pair differs for codebook {table}, tuple {packed}.");
                        }
                        Mp3BitReader exact = new(data, offset, word.Length);
                        if (Mp3Huffman.ReadPair(ref exact, table) != (x, y) || exact.Remaining != 0)
                        {
                            throw new InvalidDataException("The MP3 pair failed at the exact bit boundary.");
                        }
                    }
                }
            }
        }

        private static void VerifyQuad(int table, int packed, string code)
        {
            for (int signs = 0; signs < 16; signs++)
            {
                string word = code;
                int[] expected = new int[4];
                for (int index = 0; index < 4; index++)
                {
                    if (((packed >> (3 - index)) & 1) != 0)
                    {
                        int sign = (signs >> index) & 1;
                        word += sign == 0 ? "0" : "1";
                        expected[index] = sign == 0 ? 1 : -1;
                    }
                }
                for (int offset = 0; offset < 8; offset++)
                {
                    byte[] data = Pack(new string('1', offset) + word + "101101");
                    Mp3BitReader bits = new(data, offset, word.Length + 6);
                    int[] output = [99, 99, 99, 99, 99];
                    if (!Mp3Huffman.TryReadQuad(ref bits, table, output) || !output.AsSpan(0, 4).SequenceEqual(expected)
                        || output[4] != 99 || bits.Position != offset + word.Length || bits.Read(6) != 45)
                    {
                        throw new InvalidDataException("The MP3 quadruple or following bit field differs.");
                    }
                    Mp3BitReader exact = new(data, offset, word.Length);
                    if (!Mp3Huffman.TryReadQuad(ref exact, table, output) || exact.Remaining != 0)
                    {
                        throw new InvalidDataException("The MP3 quadruple failed at the exact bit boundary.");
                    }
                    for (int available = 0; available < word.Length; available++)
                    {
                        output.AsSpan().Fill(99);
                        Mp3BitReader partial = new(data, offset, available);
                        if (Mp3Huffman.TryReadQuad(ref partial, table, output) || partial.Position != offset || output.Any(value => value != 99))
                        {
                            throw new InvalidDataException("An incomplete MP3 quadruple changed the cursor or output.");
                        }
                    }
                }
            }
        }

        public static byte[] Pack(string bits)
        {
            string aligned = bits.PadRight((bits.Length + 7) / 8 * 8, '0');
            return Enumerable.Range(0, aligned.Length / 8)
                .Select(index => Convert.ToByte(aligned.Substring(index * 8, 8), 2)).ToArray();
        }
    }
}
