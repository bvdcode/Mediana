// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaVideoTestData
    {
        public static byte[] Document(string codec, ulong? period, params byte[][] blocks)
        {
            byte[] duration = period is ulong value ? Unsigned(0x23E383, value) : [];
            byte[] track = Field(0xAE, [.. Unsigned(0xD7, 1), .. Unsigned(0x73C5, 1), .. Unsigned(0x83, 1),
                .. Field(0x86, Encoding.UTF8.GetBytes(codec)), .. duration,
                .. Field(0xE0, [.. Unsigned(0xB0, 128), .. Unsigned(0xBA, 80)])]);
            return MatroskaReaderTestData.Document(Field(0x1549A966, InfoFields), Field(0x1654AE6B, track),
                MatroskaIndexTestData.Cluster([Unsigned(0xE7, 0), .. blocks]));
        }
    }
}
