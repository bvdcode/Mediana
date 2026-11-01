// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jpeg;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;

namespace Mediana.Tests
{
    internal static class JpegDecodeTestInput
    {
        internal static async Task VerifyCorpusAsync(string root)
        {
            foreach (string line in await File.ReadAllLinesAsync(Path.Combine(root, "corpus.tsv")))
            {
                string[] fields = line.Split('\t');
                JpegImageDecoder decoder = new();
                if (fields[0] == "embedded-cover")
                {
                    decoder = new(sourceColorSpace: JpegColorSpace.YCbCr, sampleAspectRatio: SampleAspectRatio.Square);
                }
                int width = int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
                int height = int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture);
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, fields[0] + ".jpg"));
                byte[] expected = await File.ReadAllBytesAsync(Path.Combine(root, fields[0] + ".rgba"));
                ImageInfo info = await decoder.ReadInfoAsync(encoded);
                MediaBufferAllocator allocator = new(1024 * 1024);
                using (VideoFrame frame = await decoder.DecodeAsync(encoded, new(), allocator))
                {
                    encoded.AsSpan().Clear();
                    if (info.FormatId != "jpeg" || info.Width != width || info.Height != height || frame.Format.Width != width ||
                        frame.Format.Height != height || frame.Data.Length != expected.Length || allocator.AllocatedBytes != expected.Length ||
                        frame.Format.Color.Transfer != TransferFunction.Unspecified || frame.Format.Color.Primaries != ColorPrimaries.Unspecified)
                    {
                        throw new InvalidDataException("JPEG dimensions, source labels or owned output differ: " + fields[0]);
                    }
                    int maximum = 0;
                    for (int i = 0; i < expected.Length; i++)
                    {
                        int difference = Math.Abs(frame.Data.Span[i] - expected[i]);
                        maximum = Math.Max(maximum, difference);
                        if ((i & 3) == 3 && difference != 0)
                        {
                            throw new InvalidDataException("JPEG output alpha differs from the opaque reference.");
                        }
                    }
                    if (maximum > 4)
                    {
                        throw new InvalidDataException($"JPEG independent full-pixel error exceeds four codes: {fields[0]}, {maximum}.");
                    }
                }
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("JPEG corpus retains an output or component buffer.");
                }
            }
        }

        internal static async Task VerifyIntegrityAsync(string root)
        {
            byte[] valid = JpegTestData.Image();
            for (int length = 0; length < valid.Length; length++)
            {
                await RejectAsync<InvalidDataException>(valid[..length]);
            }
            List<byte[]> invalid = [[.. valid, 0], JpegTestData.Insert(valid, 216, [])];
            byte[] zeroQuantization = valid.ToArray();
            zeroQuantization[JpegTestData.Find(valid, 219) + 5] = 0;
            invalid.Add(zeroQuantization);
            byte[] invalidPadding = valid.ToArray();
            invalidPadding[^3] = 0;
            invalid.Add(invalidPadding);
            byte[] missingDc = valid.ToArray();
            missingDc[JpegTestData.Find(valid, 218) + 6] = 17;
            invalid.Add(missingDc);
            byte[] allOnesCode = valid.ToArray();
            allOnesCode[JpegTestData.Find(valid, 196) + 5] = 2;
            invalid.Add(allOnesCode);
            byte[] invalidDcCategory = valid.ToArray();
            invalidDcCategory[JpegTestData.Find(valid, 196) + 21] = 12;
            invalid.Add(invalidDcCategory);
            byte[] invalidAcCategory = valid.ToArray();
            invalidAcCategory[JpegTestData.Find(valid, 196) + 39] = 11;
            invalid.Add(invalidAcCategory);
            byte[] invalidZeroRun = valid.ToArray();
            invalidZeroRun[JpegTestData.Find(valid, 196) + 39] = 240;
            invalidZeroRun[^3] = 7;
            invalid.Add(invalidZeroRun);
            byte[] spectral = valid.ToArray();
            spectral[JpegTestData.Find(valid, 218) + 8] = 62;
            invalid.Add(spectral);
            byte[] restart = await File.ReadAllBytesAsync(Path.Combine(root, "rgb-restarts.jpg"));
            restart[JpegTestData.Find(restart, 208) + 1] = 209;
            invalid.Add(restart);
            foreach (byte[] bytes in invalid)
            {
                await RejectAsync<InvalidDataException>(bytes);
            }
            await RejectAsync<NotSupportedException>(await File.ReadAllBytesAsync(Path.Combine(root, "unsupported-progressive.jpg")));
            await RejectAsync<NotSupportedException>(JpegTestData.Insert(valid, 225, "Exif\0\0"u8));
        }

        internal static async Task VerifyResourcesAsync()
        {
            byte[] encoded = JpegTestData.Image();
            MediaBufferAllocator exact = new(68);
            using (VideoFrame frame = await new JpegImageDecoder(new() { MaximumMarkers = 6 }).DecodeAsync(encoded, new(), exact))
            {
                if (exact.AllocatedBytes != 4 || !frame.Data.Span.SequenceEqual(new byte[] { 128, 128, 128, 255 }))
                {
                    throw new InvalidDataException("JPEG exact component/output budget or known DC samples differ.");
                }
            }
            if (exact.AllocatedBytes != 0)
            {
                throw new InvalidDataException("JPEG output lease was not released.");
            }
            MediaBufferAllocator insufficient = new(67);
            try
            {
                using VideoFrame unexpected = await new JpegImageDecoder().DecodeAsync(encoded, new(), insufficient);
                throw new InvalidDataException("JPEG accepted an insufficient simultaneous component/output budget.");
            }
            catch (MediaMemoryLimitException) { }
            if (insufficient.AllocatedBytes != 0)
            {
                throw new InvalidDataException("JPEG working storage was retained after allocation failure.");
            }
            await RejectAsync<InvalidDataException>(encoded, new() { MaximumMarkers = 5 });
            await RejectAsync<InvalidDataException>(encoded, new() { MaximumEncodedBytes = encoded.Length - 1 });
            byte[] larger = encoded.ToArray();
            larger[JpegTestData.Find(larger, 192) + 8] = 2;
            await RejectAsync<InvalidDataException>(larger, new() { MaximumPixels = 1 });
            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();
            MediaBufferAllocator allocator = new(1024);
            try
            {
                using VideoFrame unexpected = await new JpegImageDecoder().DecodeAsync(encoded, new(), allocator, cancelled.Token);
                throw new InvalidDataException("JPEG accepted cancellation.");
            }
            catch (OperationCanceledException) { }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("JPEG retained media storage after cancellation.");
            }
            byte[] icc = JpegTestData.Insert(encoded, 226, [.. "ICC_PROFILE\0"u8, 1, 1, 42]);
            using VideoFrame declared = await new JpegImageDecoder().DecodeAsync(icc, new(), allocator);
            try
            {
                declared.Format.GetColorInterpretation();
                throw new InvalidDataException("JPEG silently ignored an embedded ICC profile.");
            }
            catch (NotSupportedException) { }
        }

        internal static async Task VerifyInterpretationsAsync(string root)
        {
            byte[] unmarked = await File.ReadAllBytesAsync(Path.Combine(root, "embedded-cover.jpg"));
            await RejectAsync<NotSupportedException>(unmarked);
            byte[] tagged = await File.ReadAllBytesAsync(Path.Combine(root, "rgb-s0-7x9.jpg"));
            MediaBufferAllocator allocator = new(4096);
            bool rejected = false;
            try
            {
                using VideoFrame unexpected = await new JpegImageDecoder(sourceColorSpace: JpegColorSpace.Rgb).DecodeAsync(tagged, new(), allocator);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            if (!rejected || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("JPEG must reject conflicting caller and declared color without retaining samples.");
            }
            rejected = false;
            try
            {
                using VideoFrame unexpected = await new JpegImageDecoder(sampleAspectRatio: new SampleAspectRatio(3, 2)).DecodeAsync(tagged, new(), allocator);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            if (!rejected || allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("JPEG must reject conflicting caller and declared aspect without retaining samples.");
            }
            byte[] icc = JpegTestData.Insert(JpegTestData.Image(), 226, [.. "ICC_PROFILE\0"u8, 1, 2, 42]);
            await RejectAsync<InvalidDataException>(icc);
            byte[] precision = JpegTestData.Image();
            precision[JpegTestData.Find(precision, 192) + 4] = 12;
            await RejectAsync<NotSupportedException>(precision);
        }

        private static async Task RejectAsync<T>(byte[] encoded, JpegDecodeOptions? options = null) where T : Exception
        {
            MediaBufferAllocator allocator = new(1024 * 1024);
            try
            {
                using VideoFrame unexpected = await new JpegImageDecoder(options).DecodeAsync(encoded, new(), allocator);
            }
            catch (T)
            {
                if (allocator.AllocatedBytes != 0)
                {
                    throw new InvalidDataException("JPEG retained media storage after rejection.");
                }
                return;
            }
            throw new InvalidDataException("JPEG rejection or allocation release differs.");
        }
    }
}
