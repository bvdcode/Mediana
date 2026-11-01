// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using Mediana.Documents.Pdf;

namespace Mediana.Tests
{
    internal static class PdfStreamOwnershipTestInput
    {
        internal static async Task VerifyAsync()
        {
            byte[] samples = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120];
            (byte[] Encoded, string Filter)[] cases =
            [
                (samples, ""),
                (Deflate(samples), "/Filter /FlateDecode"),
                (PdfTestDocument.Bytes(Convert.ToHexString(samples) + ">"), "/Filter /ASCIIHexDecode"),
                ([11, .. samples, 128], "/Filter /RunLengthDecode"),
                (Deflate([1, 10, 20, 30, 30, 30, 30, 2, 60, 60, 60, 60, 60, 60]),
                    "/Filter /FlateDecode /DecodeParms << /Predictor 15 /Colors 3 /Columns 2 >>"),
                (PdfTestDocument.Bytes(Convert.ToHexString(Deflate(samples)) + ">"),
                    "/Filter [/ASCIIHexDecode /FlateDecode] /DecodeParms [null null]")
            ];
            foreach ((byte[] encoded, string filter) in cases)
            {
                byte[] image = PdfTestDocument.Stream(encoded,
                    "/Type /XObject /Subtype /Image /Width 2 /Height 2 /BitsPerComponent 8 /ColorSpace /DeviceRGB " + filter);
                byte[] source = PdfTestDocument.Create("/I Do /I Do", resources: "/XObject << /I 5 0 R >>", extraObjects: image);
                byte[] original = source.ToArray();
                using MemoryStream input = new(source, writable: true);
                using PdfDocument document = await PdfReader.ReadAsync(input,
                    new() { MaximumDecodedBytes = 32 });
                if (!source.AsSpan().SequenceEqual(original))
                {
                    throw new InvalidDataException("PDF reading changed the caller's encoded input.");
                }
                source.AsSpan().Fill(255);
                PdfPage first = document.GetPage(1);
                PdfPage second = document.GetPage(1);
                document.Dispose();
                VerifyPixels(first, samples);
                VerifyPixels(second, samples);
                if (!input.CanRead)
                {
                    throw new InvalidDataException("PDF reading closed the caller's input stream.");
                }
            }
            byte[] stencil = PdfTestDocument.Stream(Deflate([0]),
                "/Type /XObject /Subtype /Image /Width 1 /Height 1 /ImageMask true /Filter /FlateDecode");
            byte[] stencilSource = PdfTestDocument.Create("1 0 0 rg /I Do 0 0 1 rg /I Do",
                resources: "/XObject << /I 5 0 R >>", extraObjects: stencil);
            using PdfDocument stencils = await PdfReader.ReadAsync(new MemoryStream(stencilSource));
            foreach (PdfPage page in new[] { stencils.GetPage(1), stencils.GetPage(1) })
            {
                if (page.Images.Count != 2 || !page.Images[0].Pixels.Span.SequenceEqual(new byte[] { 255, 0, 0, 255 })
                    || !page.Images[1].Pixels.Span.SequenceEqual(new byte[] { 0, 0, 255, 255 }))
                {
                    throw new InvalidDataException("Repeated PDF stencil decoding changed cached samples or invocation colors.");
                }
            }
        }

        internal static Task<long> MeasureUnfilteredAsync() => MeasureAsync(null);

        internal static Task<long> MeasureFlateAsync(CompressionLevel compression) => MeasureAsync(compression);

        private static async Task<long> MeasureAsync(CompressionLevel? compression)
        {
            const int Width = 1000;
            const int Height = 500;
            const int Repetitions = 3;
            byte[] samples = new byte[Width * Height * 3];
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (byte)(i * 13 + i / 23);
            }
            byte[] encoded = samples;
            string filter = string.Empty;
            if (compression is CompressionLevel level)
            {
                encoded = Deflate(samples, level);
                filter = "/Filter /FlateDecode";
            }
            byte[] image = PdfTestDocument.Stream(encoded,
                $"/Type /XObject /Subtype /Image /Width {Width} /Height {Height} /BitsPerComponent 8 /ColorSpace /DeviceRGB {filter}");
            byte[] input = PdfTestDocument.Create("/I Do /I Do", resources: "/XObject << /I 5 0 R >>", extraObjects: image);
            for (int warmup = 0; warmup < Repetitions; warmup++)
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
                VerifyPixels(document.GetPage(1), samples);
            }
            long allocated = 0;
            for (int iteration = 0; iteration < Repetitions; iteration++)
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(input));
                long before = GC.GetTotalAllocatedBytes(precise: true);
                PdfPage page = document.GetPage(1);
                allocated += GC.GetTotalAllocatedBytes(precise: true) - before;
                document.Dispose();
                VerifyPixels(page, samples);
            }
            return allocated / Repetitions;
        }

        internal static async Task VerifyAllocationAsync()
        {
            long allocated = await MeasureUnfilteredAsync();
            const long Maximum = 2_000_000 + 750_000;
            if (allocated >= Maximum)
            {
                throw new InvalidDataException($"PDF page interpretation allocated {allocated} bytes; "
                    + $"the {Maximum}-byte limit excludes a duplicate complete RGB stream.");
            }
        }

        internal static async Task VerifyFlateAllocationAsync()
        {
            long compressed = await MeasureFlateAsync(CompressionLevel.SmallestSize);
            long stored = await MeasureFlateAsync(CompressionLevel.NoCompression);
            const long MaximumDifference = 750_000;
            if (stored - compressed >= MaximumDifference)
            {
                throw new InvalidDataException($"Equivalent PDF Flate streams allocated {compressed} and {stored} bytes; "
                    + $"the {MaximumDifference}-byte difference limit excludes a complete stored-stream input copy.");
            }
        }

        private static byte[] Deflate(ReadOnlySpan<byte> samples, CompressionLevel compression = CompressionLevel.SmallestSize)
        {
            using MemoryStream output = new();
            using (ZLibStream zip = new(output, compression, leaveOpen: true))
            {
                zip.Write(samples);
            }
            return output.ToArray();
        }

        private static void VerifyPixels(PdfPage page, ReadOnlySpan<byte> samples)
        {
            if (page.Images.Count != 1 || (long)page.Images[0].Width * page.Images[0].Height * 3 != samples.Length)
            {
                throw new InvalidDataException("PDF image reuse or sample geometry differs.");
            }
            ReadOnlySpan<byte> pixels = page.Images[0].Pixels.Span;
            for (int i = 0; i < samples.Length / 3; i++)
            {
                if (pixels[i * 4] != samples[i * 3] || pixels[i * 4 + 1] != samples[i * 3 + 1]
                    || pixels[i * 4 + 2] != samples[i * 3 + 2] || pixels[i * 4 + 3] != 255)
                {
                    throw new InvalidDataException("PDF pixels differ from the independent RGB source after document disposal.");
                }
            }
        }
    }
}
