// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.IO.Compression;
using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class PdfContentOwnershipTestInput
    {
        internal static readonly byte[] Painting = PdfTestDocument.Bytes("0 0 1 rg 0 0 200 100 re f");

        internal static byte[] Create(byte[][] contents, bool indirectArray = false, bool flate = false)
        {
            string[] references = Enumerable.Range(4, contents.Length)
                .Select(number => number.ToString(CultureInfo.InvariantCulture) + " 0 R").ToArray();
            string array = "[" + string.Join(" ", references) + "]";
            string selection = contents.Length == 1 ? references[0] : array;
            if (indirectArray)
            {
                selection = (4 + contents.Length).ToString(CultureInfo.InvariantCulture) + " 0 R";
            }
            List<byte[]> objects =
            [
                PdfTestDocument.Bytes("<< /Type /Catalog /Pages 2 0 R >>"),
                PdfTestDocument.Bytes("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
                PdfTestDocument.Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Resources << >> /Contents " + selection + " >>")
            ];
            foreach (byte[] content in contents)
            {
                if (flate)
                {
                    using MemoryStream encoded = new();
                    using (ZLibStream compression = new(encoded, CompressionLevel.SmallestSize, leaveOpen: true))
                    {
                        compression.Write(content);
                    }
                    objects.Add(PdfTestDocument.Stream(encoded.ToArray(), "/Filter /FlateDecode"));
                }
                else
                {
                    objects.Add(PdfTestDocument.Stream(content));
                }
            }
            if (indirectArray)
            {
                objects.Add(PdfTestDocument.Bytes(array));
            }
            return PdfTestDocument.Write(objects);
        }

        internal static async Task VerifyAsync()
        {
            byte[][] contents =
            [
                PdfTestDocument.Bytes("q 1 0 0 rg 0 0 200 100 re f\n"),
                PdfTestDocument.Bytes("Q\n0 0 "),
                PdfTestDocument.Bytes("1 rg 0 0 200 100 re f% final comment")
            ];
            foreach (bool indirect in new[] { false, true })
            {
                foreach (bool flate in new[] { false, true })
                {
                    byte[] source = Create(contents, indirect, flate);
                    using MemoryStream input = new(source, writable: true);
                    using PdfDocument document = await PdfReader.ReadAsync(input,
                        new() { MaximumDecodedBytes = contents.Sum(content => content.Length) });
                    source.AsSpan().Fill(255);
                    PdfPage first = document.GetPage(1);
                    PdfPage second = document.GetPage(1);
                    document.Dispose();
                    VerifyPainting(first);
                    VerifyPainting(second);
                }
            }
            foreach (bool array in new[] { false, true })
            {
                using PdfDocument single = await PdfReader.ReadAsync(new MemoryStream(Create([Painting], array)),
                    new() { MaximumDecodedBytes = Painting.Length });
                VerifyPainting(single.GetPage(1));
            }
        }

        internal static byte[] CreatePadded(int streamCount, int payloadBytes)
        {
            byte[] content = new byte[payloadBytes];
            content.AsSpan().Fill((byte)' ');
            content[payloadBytes - Painting.Length - 1] = (byte)'\n';
            Painting.CopyTo(content, payloadBytes - Painting.Length);
            byte[][] parts = new byte[streamCount][];
            for (int index = 0; index < streamCount; index++)
            {
                int first = index * payloadBytes / streamCount;
                int last = (index + 1) * payloadBytes / streamCount;
                parts[index] = content.AsSpan(first, last - first).ToArray();
            }
            return Create(parts);
        }

        internal static async Task<long> MeasureAsync(int streamCount)
        {
            const int Iterations = 3;
            byte[] input = CreatePadded(streamCount, 1024 * 1024);
            long allocated = 0;
            for (int iteration = -Iterations; iteration < Iterations; iteration++)
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
                long before = GC.GetTotalAllocatedBytes(precise: true);
                PdfPage page = document.GetPage(1);
                long bytes = GC.GetTotalAllocatedBytes(precise: true) - before;
                if (iteration >= 0)
                {
                    allocated += bytes;
                }
                VerifyPainting(page);
            }
            return allocated / Iterations;
        }

        internal static async Task VerifyAllocationAsync()
        {
            foreach (int count in new[] { 1, 2, 17 })
            {
                long allocated = await MeasureAsync(count);
                long maximum = (count == 1 ? 0 : 1024 * 1024) + 128 * 1024;
                if (allocated >= maximum)
                {
                    throw new InvalidDataException($"PDF interpretation of {count} content streams allocated {allocated} bytes; limit {maximum} excludes redundant complete content buffers.");
                }
            }
        }

        private static void VerifyPainting(PdfPage page)
        {
            using PixelBuffer pixels = PdfRenderer.Render(page, new() { Dpi = 72, Supersampling = 1 });
            for (int y = 0; y < pixels.Height; y++)
            {
                for (int x = 0; x < pixels.Width; x++)
                {
                    if (pixels[x, y] != new Rgba32(0, 0, 255))
                    {
                        throw new InvalidDataException("PDF content stream state, operands or detached painting differ.");
                    }
                }
            }
        }
    }
}
