// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Documents.Pdf;

namespace Mediana.Tests
{
    internal static class PdfByteFilterTestInput
    {
        internal static async Task VerifyAsciiHexAsync()
        {
            foreach (string filter in new[] { "ASCIIHexDecode", "AHx" })
            {
                await VerifyAsync(filter, Encoding.ASCII.GetBytes("\0\t aB\nCd\fE\r >ignored"), [171, 205, 224]);
                await VerifyEmptyAsync(filter, ">ignored"u8.ToArray());
            }
        }

        internal static async Task VerifyAscii85Async()
        {
            foreach (string filter in new[] { "ASCII85Decode", "A85" })
            {
                await VerifyAsync(filter, " \0\tz\r\n!<N?+\f~>ignored"u8.ToArray(), [0, 0, 0, 0, 1, 2, 3, 4]);
                await VerifyAsync(filter, "!!~>"u8.ToArray(), [0]);
                await VerifyAsync(filter, "!!!~>"u8.ToArray(), [0, 0]);
                await VerifyAsync(filter, "!!!!~>"u8.ToArray(), [0, 0, 0]);
                await VerifyAsync(filter, "s8W-!~>"u8.ToArray(), [255, 255, 255, 255]);
                await VerifyEmptyAsync(filter, "~>ignored"u8.ToArray());
            }
        }

        internal static async Task VerifyHexBlocksAsync()
        {
            byte[] samples = new byte[8201];
            for (int i = 0; i < samples.Length; i++) { samples[i] = (byte)i; }
            string digits = Convert.ToHexString(samples);
            foreach (int split in new[] { 1, 8191, 8192, 8193 })
            {
                await VerifyAsync("ASCIIHexDecode", Encoding.ASCII.GetBytes(digits[..split] + "\0 \r\n" + digits[split..] + ">"), samples);
            }
        }

        internal static async Task VerifyRunLengthAsync()
        {
            byte[] literal = Enumerable.Range(0, 128).Select(x => (byte)x).ToArray();
            foreach (string filter in new[] { "RunLengthDecode", "RL" })
            {
                await VerifyAsync(filter, [127, .. literal, 128, 255], literal);
                await VerifyAsync(filter, [0, 127, 129, 5, 255, 6, 128, 255],
                    [127, .. Enumerable.Repeat((byte)5, 128), 6, 6]);
                await VerifyEmptyAsync(filter, [128, 255]);
            }
        }

        internal static async Task VerifyInvalidAsync()
        {
            (string Filter, byte[] Encoded)[] invalid =
            [
                ("ASCIIHexDecode", "01"u8.ToArray()), ("ASCIIHexDecode", "g>"u8.ToArray()),
                ("ASCII85Decode", "z"u8.ToArray()), ("ASCII85Decode", "!~>"u8.ToArray()),
                ("ASCII85Decode", "!z~>"u8.ToArray()), ("ASCII85Decode", "v~>"u8.ToArray()),
                ("ASCII85Decode", "uuuuu~>"u8.ToArray()), ("ASCII85Decode", "uu~>"u8.ToArray()),
                ("ASCII85Decode", "~"u8.ToArray()), ("ASCII85Decode", "~ >"u8.ToArray()),
                ("RunLengthDecode", [0]), ("RunLengthDecode", [127, 1]),
                ("RunLengthDecode", [129]), ("RunLengthDecode", [255, 0])
            ];
            foreach ((string filter, byte[] encoded) in invalid)
            {
                using PdfDocument document = await OpenImageAsync(encoded, filter, 1, 1, 100);
                bool rejected = false;
                try { document.GetPage(1); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected) { throw new InvalidDataException("A malformed PDF byte filter was accepted."); }
            }
        }

        internal static async Task VerifyLimitsAsync(string filter)
        {
            byte[] samples = new byte[256];
            byte[] encoded = EncodeZeros(samples.Length, filter);
            using (PdfDocument exact = await OpenImageAsync(encoded, filter, 256, 1, 261))
            {
                VerifyPixels(exact.GetPage(1), samples);
            }
            using (PdfDocument limited = await OpenImageAsync(encoded, filter, 256, 1, 255))
            {
                bool rejected = false;
                try { limited.GetPage(1); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected) { throw new InvalidDataException("A PDF byte filter exceeded its decoded byte limit."); }
            }
            using PdfDocument cancelled = await OpenImageAsync(encoded, filter, 256, 1, 261);
            using CancellationTokenSource cancellation = new();
            await cancellation.CancelAsync();
            bool stopped = false;
            try { cancelled.GetPage(1, cancellation.Token); }
            catch (OperationCanceledException) { stopped = true; }
            if (!stopped) { throw new InvalidDataException("Cancelled PDF byte-filter decoding continued."); }
        }

        internal static async Task VerifyAllocationAsync(string filter)
        {
            long allocated = await MeasureAsync(filter);
            const long Maximum = 5_500_000;
            if (allocated >= Maximum)
            {
                throw new InvalidDataException($"PDF {filter} interpretation allocated {allocated} bytes; "
                    + $"the {Maximum}-byte limit excludes a second complete decoded payload.");
            }
        }

        internal static async Task VerifyRejectedAllocationAsync(string filter, bool malformed)
        {
            const int Size = 1024 * 1024;
            byte[] encoded = EncodeZeros(Size, filter);
            int limit;
            if (malformed)
            {
                int marker = filter switch { "ASCIIHexDecode" => 1, "ASCII85Decode" => 2, "RunLengthDecode" => 1, _ => throw new ArgumentOutOfRangeException(nameof(filter)) };
                encoded = encoded[..^marker];
                limit = Size + 5;
            }
            else { limit = 1024; }
            using PdfDocument document = await OpenImageAsync(encoded, filter, 1024, 1024, limit);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool rejected = false;
            try { document.GetPage(1); }
            catch (InvalidDataException) { rejected = true; }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (!rejected || allocated >= 128 * 1024)
            {
                throw new InvalidDataException($"Rejected PDF {filter} data allocated {allocated} bytes instead of failing before decoded payload allocation.");
            }
        }

        internal static async Task<long> MeasureAsync(string filter)
        {
            const int Width = 1024;
            const int Height = 1024;
            byte[] samples = new byte[Width * Height];
            byte[] encoded = EncodeZeros(samples.Length, filter);
            byte[] source = CreateImage(encoded, filter, Width, Height);
            long allocated = 0;
            for (int iteration = 0; iteration < 5; iteration++)
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(source));
                long before = GC.GetAllocatedBytesForCurrentThread();
                PdfPage page = document.GetPage(1);
                long current = GC.GetAllocatedBytesForCurrentThread() - before;
                document.Dispose();
                VerifyPixels(page, samples);
                if (iteration >= 2) { allocated += current; }
            }
            return allocated / 3;
        }

        private static async Task VerifyAsync(string filter, byte[] encoded, byte[] expected)
        {
            byte[] original = encoded.ToArray();
            using PdfDocument document = await OpenImageAsync(encoded, filter, expected.Length, 1, expected.Length + 5);
            PdfPage first = document.GetPage(1);
            PdfPage second = document.GetPage(1);
            document.Dispose();
            VerifyPixels(first, expected);
            VerifyPixels(second, expected);
            if (!encoded.AsSpan().SequenceEqual(original)) { throw new InvalidDataException("PDF filter decoding changed the encoded input."); }
        }

        private static async Task VerifyEmptyAsync(string filter, byte[] encoded)
        {
            byte[] source = PdfTestDocument.Write([
                PdfTestDocument.Bytes("<< /Type /Catalog /Pages 2 0 R >>"),
                PdfTestDocument.Bytes("<< /Type /Pages /Count 1 /Kids [3 0 R] >>"),
                PdfTestDocument.Bytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] /Contents 4 0 R >>"),
                PdfTestDocument.Stream(encoded, "/Filter /" + filter)]);
            using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(source), new() { MaximumDecodedBytes = 1 });
            if (document.GetPage(1).Images.Count != 0) { throw new InvalidDataException("An empty PDF content stream produced an image."); }
        }

        private static Task<PdfDocument> OpenImageAsync(byte[] encoded, string filter, int width, int height, int limit) =>
            PdfReader.ReadAsync(new MemoryStream(CreateImage(encoded, filter, width, height)), new() { MaximumDecodedBytes = limit });

        private static byte[] CreateImage(byte[] encoded, string filter, int width, int height) =>
            PdfTestDocument.Create("/I Do", resources: "/XObject << /I 5 0 R >>", extraObjects:
                PdfTestDocument.Stream(encoded, $"/Type /XObject /Subtype /Image /Width {width} /Height {height} /BitsPerComponent 8 /ColorSpace /DeviceGray /Filter /{filter}"));

        private static byte[] EncodeZeros(int length, string filter)
        {
            switch (filter)
            {
                case "ASCIIHexDecode": return Encoding.ASCII.GetBytes(new string('0', length * 2) + ">");
                case "ASCII85Decode": return Encoding.ASCII.GetBytes(new string('z', length / 4) + "~>");
                case "RunLengthDecode":
                    byte[] result = new byte[length / 128 * 2 + 1];
                    for (int i = 0; i < result.Length - 1; i += 2) { result[i] = 129; }
                    result[^1] = 128;
                    return result;
                default: throw new ArgumentOutOfRangeException(nameof(filter));
            }
        }

        private static void VerifyPixels(PdfPage page, ReadOnlySpan<byte> samples)
        {
            if (page.Images.Count != 1 || page.Images[0].Pixels.Length != samples.Length * 4)
            {
                throw new InvalidDataException("Decoded PDF byte-filter image geometry differs.");
            }
            ReadOnlySpan<byte> pixels = page.Images[0].Pixels.Span;
            for (int i = 0; i < samples.Length; i++)
            {
                if (pixels[i * 4] != samples[i] || pixels[i * 4 + 1] != samples[i]
                    || pixels[i * 4 + 2] != samples[i] || pixels[i * 4 + 3] != 255)
                {
                    throw new InvalidDataException("Decoded PDF byte-filter pixels differ from the source bytes.");
                }
            }
        }
    }
}
