// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaAttachmentTestData
    {
        public static byte[][] Fields => [Field(0x466E, "cover.png"u8), Field(0x4660, "image/png"u8),
            Unsigned(0x46AE, ulong.MaxValue), Field(0x465C, [1, 2, 3])];
        public static byte[] Entry(params byte[][] fields) => Field(0x61A7, fields.SelectMany(field => field).ToArray());
        public static byte[] Attachments(params byte[][] entries) => Field(0x1941A469, entries.SelectMany(entry => entry).ToArray());
    }
}
