// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Dts
{
    internal static class DtsCodebooks
    {
        public static readonly DtsHuffmanCodebook[] Transient = [new(DtsHuffmanTables.A4), new(DtsHuffmanTables.B4), new(DtsHuffmanTables.C4), new(DtsHuffmanTables.D4)];
        public static readonly DtsHuffmanCodebook[] Allocation = [new(DtsHuffmanTables.A12), new(DtsHuffmanTables.B12), new(DtsHuffmanTables.C12), new(DtsHuffmanTables.D12), new(DtsHuffmanTables.E12)];
        public static readonly DtsHuffmanCodebook[] Scale = [new(DtsHuffmanTables.SA129), new(DtsHuffmanTables.SB129), new(DtsHuffmanTables.SC129), new(DtsHuffmanTables.SD129), new(DtsHuffmanTables.SE129)];
        public static readonly DtsHuffmanCodebook[][] Samples =
        [
            [],
            [new(DtsHuffmanTables.A3)],
            [new(DtsHuffmanTables.A5), new(DtsHuffmanTables.B5), new(DtsHuffmanTables.C5)],
            [new(DtsHuffmanTables.A7), new(DtsHuffmanTables.B7), new(DtsHuffmanTables.C7)],
            [new(DtsHuffmanTables.A9), new(DtsHuffmanTables.B9), new(DtsHuffmanTables.C9)],
            [new(DtsHuffmanTables.A13), new(DtsHuffmanTables.B13), new(DtsHuffmanTables.C13)],
            [new(DtsHuffmanTables.A17), new(DtsHuffmanTables.B17), new(DtsHuffmanTables.C17), new(DtsHuffmanTables.D17), new(DtsHuffmanTables.E17), new(DtsHuffmanTables.F17), new(DtsHuffmanTables.G17)],
            [new(DtsHuffmanTables.A25), new(DtsHuffmanTables.B25), new(DtsHuffmanTables.C25), new(DtsHuffmanTables.D25), new(DtsHuffmanTables.E25), new(DtsHuffmanTables.F25), new(DtsHuffmanTables.G25)],
            [new(DtsHuffmanTables.A33), new(DtsHuffmanTables.B33), new(DtsHuffmanTables.C33), new(DtsHuffmanTables.D33), new(DtsHuffmanTables.E33), new(DtsHuffmanTables.F33), new(DtsHuffmanTables.G33)],
            [new(DtsHuffmanTables.A65), new(DtsHuffmanTables.B65), new(DtsHuffmanTables.C65), new(DtsHuffmanTables.D65), new(DtsHuffmanTables.E65), new(DtsHuffmanTables.F65), new(DtsHuffmanTables.G65)],
            [new(DtsHuffmanTables.A129), new(DtsHuffmanTables.B129), new(DtsHuffmanTables.C129), new(DtsHuffmanTables.D129), new(DtsHuffmanTables.E129), new(DtsHuffmanTables.F129), new(DtsHuffmanTables.G129)],
        ];
    }
}
