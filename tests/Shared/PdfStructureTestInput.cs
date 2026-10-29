// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using Mediana.Documents.Pdf;
using Mediana.Drawing;
namespace Mediana.Tests
{
    internal static class PdfStructureTestInput
    {
        internal static async Task VerifyAsync()
        {
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(CompressedObjects()));
            PdfPage page = document.GetPage(1);
            PixelBuffer image = PdfRenderer.Render(page, new() { Dpi = 72, Supersampling = 1 });
            if (image.Width != 200 || image.Height != 100 || image[15, 85] != new Rgba32(255, 0, 0))
            { throw new InvalidDataException("Compressed PDF objects or cross-reference stream differ."); }
        }
        internal static byte[] CompressedObjects()
        {
            string[] bodies = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << >> /Contents 4 0 R >>"];
            string header = string.Empty, content = string.Empty;
            for (int i = 0; i < bodies.Length; i++)
            {
                header += $"{i + 1} {content.Length} "; content += bodies[i] + " ";
            }
            using MemoryStream encoded = new();
            using (ZLibStream deflater = new(encoded, CompressionLevel.SmallestSize, true)) { deflater.Write(PdfTestDocument.Bytes(header + content)); }
            using MemoryStream output = new();
            output.Write(PdfTestDocument.Bytes("%PDF-1.7\n"));
            int fourth = checked((int)output.Position);
            output.Write(PdfTestDocument.Bytes("4 0 obj\n")); output.Write(PdfTestDocument.Stream(PdfTestDocument.Bytes("1 0 0 rg 10 10 20 20 re f"))); output.Write(PdfTestDocument.Bytes("\nendobj\n"));
            int fifth = checked((int)output.Position);
            output.Write(PdfTestDocument.Bytes("5 0 obj\n")); output.Write(PdfTestDocument.Stream(encoded.ToArray(), $"/Type /ObjStm /N 3 /First {header.Length} /Filter /FlateDecode")); output.Write(PdfTestDocument.Bytes("\nendobj\n"));
            int sixth = checked((int)output.Position);
            byte[] entries = new byte[7 * 7];
            void Entry(int number, byte kind, int field, ushort index)
            {
                entries[number * 7] = kind;
                BinaryPrimitives.WriteUInt32BigEndian(entries.AsSpan(number * 7 + 1), (uint)field);
                BinaryPrimitives.WriteUInt16BigEndian(entries.AsSpan(number * 7 + 5), index);
            }
            Entry(0, 0, 0, 65535); Entry(1, 2, 5, 0); Entry(2, 2, 5, 1); Entry(3, 2, 5, 2);
            Entry(4, 1, fourth, 0); Entry(5, 1, fifth, 0); Entry(6, 1, sixth, 0);
            output.Write(PdfTestDocument.Bytes("6 0 obj\n")); output.Write(PdfTestDocument.Stream(entries, "/Type /XRef /Size 7 /W [1 4 2] /Root 1 0 R"));
            output.Write(PdfTestDocument.Bytes("\nendobj\nstartxref\n" + sixth.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n"));
            return output.ToArray();
        }
    }
}
