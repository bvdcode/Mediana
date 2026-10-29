// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Mediana.Codecs.Jbig2
{
    // Normative numerical data: ITU-T T.88 Annex B and T.6 Tables 2 and 3.
    internal static class StandardHuffman
    {
        internal static readonly HuffmanTable[] Tables = Load();
        internal static IEnumerable<string[]> Rows(string name)
        {
            using StringReader reader = new(name switch
            {
                "Fax" => FaxCodeData.Text,
                "Huffman" => HuffmanCodeData.Text,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            });
            reader.ReadLine();
            while (reader.ReadLine() is string line)
            {
                yield return line.Split(',');
            }
        }

        internal static int Number(string value) => int.Parse(value, CultureInfo.InvariantCulture);
        private static HuffmanTable[] Load()
        {
            List<HuffmanRow>[]? rows = new List<HuffmanRow>[16];
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new List<HuffmanRow>();
            }

            foreach (string[] row in Rows("Huffman"))
            {
                rows[Number(row[0])].Add(new HuffmanRow(Number(row[1]), Number(row[2]), Number(row[3]), row[4] == "1"));
            }

            return Array.ConvertAll(rows, r => new HuffmanTable(r));
        }
    }
}
