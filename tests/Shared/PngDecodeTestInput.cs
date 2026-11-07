// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using Mediana.Codecs;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using static Mediana.Tests.PngDecodeTestData;

namespace Mediana.Tests
{
    internal static class PngDecodeTestInput
    {
        public static async Task VerifyCorpusAsync(string directory)
        {
            string[] entries = await System.IO.File.ReadAllLinesAsync(Path.Combine(directory, "corpus.tsv"));
            foreach (string entry in entries)
            {
                string[] values = entry.Split('\t');
                int width = int.Parse(values[1], CultureInfo.InvariantCulture);
                int height = int.Parse(values[2], CultureInfo.InvariantCulture);
                byte[] encoded = await System.IO.File.ReadAllBytesAsync(Path.Combine(directory, values[0] + ".png"));
                byte[] expected = await System.IO.File.ReadAllBytesAsync(Path.Combine(directory, values[0] + ".rgba"));
                PngImageDecoder decoder = new();
                ImageInfo info = await decoder.ReadInfoAsync(encoded);
                Require(info.FormatId == "png" && info.Width == width && info.Height == height && info.Duration is null);
                MediaBufferAllocator allocator = new(65536);
                using (VideoFrame frame = await decoder.DecodeAsync(encoded, new(), allocator))
                {
                    Require(frame.Data.Span.SequenceEqual(expected), values[0]);
                    Require(frame.Format.Width == width && frame.Format.Height == height && frame.Format.SampleAspectRatio == new SampleAspectRatio(3, 2));
                    Require(frame.Format.GetColorInterpretation() == VideoColorInfo.Srgb && allocator.AllocatedBytes == width * height * 4);
                    encoded.AsSpan().Clear();
                    Require(frame.Data.Span.SequenceEqual(expected));
                }
                Require(allocator.AllocatedBytes == 0);
            }
        }

        public static async Task VerifyReducedCorpusAsync(string directory)
        {
            foreach (string entry in await System.IO.File.ReadAllLinesAsync(Path.Combine(directory, "corpus.tsv")))
            {
                string[] values = entry.Split('\t');
                int width = int.Parse(values[1], CultureInfo.InvariantCulture), height = int.Parse(values[2], CultureInfo.InvariantCulture);
                byte[] encoded = await System.IO.File.ReadAllBytesAsync(Path.Combine(directory, values[0] + ".png"));
                byte[] expected = await System.IO.File.ReadAllBytesAsync(Path.Combine(directory, values[0] + ".rgba"));
                foreach (int factor in new[] { 2, 3, 8 })
                {
                    int outputWidth = (width - 1) / factor + 1, outputHeight = (height - 1) / factor + 1;
                    MediaBufferAllocator allocator = new(65536);
                    using (VideoFrame frame = await new PngImageDecoder(new() { ReductionFactor = factor }).DecodeAsync(encoded, new(), allocator))
                    {
                        Require(frame.Format.Width == outputWidth && frame.Format.Height == outputHeight, values[0]);
                        for (int y = 0; y < outputHeight; y++)
                        {
                            for (int x = 0; x < outputWidth; x++)
                            {
                                Require(frame.Data.Span.Slice((y * outputWidth + x) * 4, 4)
                                    .SequenceEqual(expected.AsSpan((y * factor * width + x * factor) * 4, 4)), values[0]);
                            }
                        }
                        Require(allocator.AllocatedBytes == outputWidth * outputHeight * 4);
                        SampleAspectRatio aspect = frame.Format.SampleAspectRatio ?? throw new InvalidDataException("Missing reduced aspect ratio.");
                        Require((long)outputWidth * aspect.Numerator * height * 2 == (long)width * 3 * outputHeight * aspect.Denominator);
                    }
                    Require(allocator.AllocatedBytes == 0);
                }
            }
            byte[] truncated = File(Chunk("IHDR", Header(2, 2)), Chunk("IDAT", Compressed([0, 1, 2, 3, 4, 1, 2, 3, 4])), Chunk("IEND", []));
            await RejectAsync<InvalidDataException>(truncated, new() { ReductionFactor = 2 });
            byte[] invalidSkippedPalette = File(Chunk("IHDR", Header(1, 2, color: 3)), Chunk("PLTE", [255, 0, 0]),
                Chunk("IDAT", Compressed([0, 0, 0, 1])), Chunk("IEND", []));
            await RejectAsync<InvalidDataException>(invalidSkippedPalette, new() { ReductionFactor = 2 });
            byte[] unknownAspect = File(Chunk("IHDR", Header(3, 1)), Chunk("pHYs", new byte[9]), Chunk("IDAT", Compressed([0, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255])), Chunk("IEND", []));
            MediaBufferAllocator budget = new(100);
            using (VideoFrame frame = await new PngImageDecoder(new() { ReductionFactor = 2 }).DecodeAsync(unknownAspect, new(), budget))
            {
                Require(frame.Format.Width == 2 && frame.Format.Height == 1 && frame.Format.SampleAspectRatio is null);
            }
            Require(budget.AllocatedBytes == 0);
            await RejectAsync<MediaMemoryLimitException>(Image([0, 1, 2, 3, 4]), new() { ReductionFactor = 2 }, budget: 15);
        }

        public static async Task VerifyIntegrityAsync()
        {
            byte[] header = Chunk("IHDR", Header());
            byte[] compressed = Compressed([0, 1, 2, 3, 4]);
            byte[] idat = Chunk("IDAT", compressed);
            byte[] end = Chunk("IEND", []);
            byte[][] invalid =
            [
                File(header, idat),
                File(idat, header, end),
                File(header, header, idat, end),
                File(header, end),
                File(header, idat, end, Chunk("tEXt", "key\0value"u8)),
                File(header, Chunk("IDAT", compressed.AsSpan(0, 2)), Chunk("tEXt", "x\0y"u8), Chunk("IDAT", compressed.AsSpan(2)), end),
                Image([5, 1, 2, 3, 4]),
                Image([0, 1, 2, 3, 4, 5]),
                Image([0, 1, 2, 3]),
                File(header, idat, Chunk("sRGB", [0]), end),
                Image([0, 1, 2, 3, 4], Chunk("sRGB", [0]), Chunk("sRGB", [0])),
                File(Chunk("IHDR", Header(color: 3)), idat, end),
                File(Chunk("IHDR", Header(color: 3)), Chunk("PLTE", [1, 2, 3]), Chunk("IDAT", Compressed([0, 1])), end),
                File(Chunk("IHDR", Header(depth: 1, color: 0)), Chunk("PLTE", [1, 2, 3]), Chunk("IDAT", Compressed([0, 0])), end),
                Image([0, 1, 2, 3, 4], Chunk("tRNS", [0, 1])),
                File(header, Chunk("PLTE", [1, 2]), idat, end),
                Image([0, 1, 2, 3, 4], Chunk("pHYs", [0])),
                Image([0, 1, 2, 3, 4], Chunk("cICP", [1, 13, 1, 1]))
            ];
            foreach (byte[] data in invalid)
            {
                await RejectAsync<InvalidDataException>(data);
            }
            for (int cut = 1; cut <= compressed.Length; cut++)
            {
                await RejectAsync<InvalidDataException>(File(header, Chunk("IDAT", compressed.AsSpan(0, compressed.Length - cut)), end));
            }
            byte[] wrongAdler = (byte[])compressed.Clone();
            wrongAdler[^1] ^= 1;
            await RejectAsync<InvalidDataException>(File(header, Chunk("IDAT", wrongAdler), end));
            byte[] wrongCrc = File(header, idat, end);
            wrongCrc[^1] ^= 1;
            await RejectAsync<InvalidDataException>(wrongCrc);
            await RejectAsync<NotSupportedException>(Image([0, 1, 2, 3, 4], Chunk("ABCD", [])));
            await RejectAsync<NotSupportedException>(Image([0, 1, 2, 3, 4], Chunk("acTL", [0, 0, 0, 1, 0, 0, 0, 0])));
            await RejectAsync<InvalidDataException>(Image([0, 1, 2, 3, 4], Chunk("eXIf", [1])));
            byte[] valid = File(header, Chunk("IDAT", compressed.AsSpan(0, 1)), Chunk("IDAT", []),
                Chunk("IDAT", [.. compressed.AsSpan(1), 1, 2, 3]), Chunk("abca", []), end);
            MediaBufferAllocator allocator = new(65536);
            using (VideoFrame frame = await new PngImageDecoder().DecodeAsync(valid, new(), allocator))
            {
                Require(frame.Data.Span.SequenceEqual(new byte[] { 1, 2, 3, 4 }));
            }
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyBudgetsAndColorAsync()
        {
            byte[] encoded = Image([0, 1, 2, 3, 4]);
            await RejectAsync<NotSupportedException>(encoded, new() { MaximumEncodedBytes = encoded.Length - 1 });
            await RejectAsync<NotSupportedException>(encoded, new() { MaximumChunks = 2 });
            await RejectAsync<NotSupportedException>(File(Chunk("IHDR", Header(2, 2)), Chunk("IDAT", []), Chunk("IEND", [])),
                new() { MaximumPixels = 3 });
            MediaBufferAllocator exact = new(12);
            using (VideoFrame frame = await new PngImageDecoder().DecodeAsync(encoded, new(), exact))
            {
                Require(exact.AllocatedBytes == 4 && frame.Format.SampleAspectRatio == SampleAspectRatio.Square);
                Require(frame.Format.Color.Primaries == ColorPrimaries.Unspecified && frame.Format.Color.Transfer == TransferFunction.Unspecified);
            }
            Require(exact.AllocatedBytes == 0);
            await RejectAsync<MediaMemoryLimitException>(encoded, budget: 11);
            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();
            await RejectAsync<OperationCanceledException>(encoded, token: cancelled.Token);
            PngImageDecoder decoder = new();
            Require(decoder.CanDecode(encoded) && !decoder.CanDecode(encoded.AsSpan(0, 7)));
            byte[] cicp = Image([0, 1, 2, 3, 4], Chunk("cICP", [12, 13, 0, 1]), Chunk("sRGB", [0]), Chunk("gAMA", [0, 0, 177, 143]));
            using (VideoFrame frame = await decoder.DecodeAsync(cicp, new(), exact))
            {
                Require(frame.Format.ColorDescription is CicpColorDescription);
                Require(frame.Format.GetColorInterpretation() == VideoColorInfo.Srgb with { Primaries = ColorPrimaries.DisplayP3 });
            }
            byte[] gamma = Image([0, 1, 2, 3, 4], Chunk("gAMA", [0, 0, 177, 143]));
            using (VideoFrame frame = await decoder.DecodeAsync(gamma, new(), exact))
            {
                Require(frame.Format.ColorDescription is PngColorDescription { Gamma: 45455, HasEmbeddedIccProfile: false });
            }
            byte[] icc = Image([0, 1, 2, 3, 4], Chunk("iCCP", [.. "Profile"u8, 0, 0, .. Compressed([1, 2, 3])]), Chunk("sRGB", [0]));
            using (VideoFrame frame = await decoder.DecodeAsync(icc, new(), exact))
            {
                Require(frame.Format.ColorDescription is PngColorDescription { HasEmbeddedIccProfile: true });
            }
            Require(exact.AllocatedBytes == 0);
        }

        private static async Task RejectAsync<T>(byte[] data, PngDecodeOptions? options = null, long budget = 65536,
            CancellationToken token = default) where T : Exception
        {
            MediaBufferAllocator allocator = new(budget);
            try
            {
                using VideoFrame frame = await new PngImageDecoder(options).DecodeAsync(data, new(), allocator, token);
            }
            catch (T)
            {
                Require(allocator.AllocatedBytes == 0);
                return;
            }
            throw new InvalidDataException("PNG input was accepted unexpectedly.");
        }

        private static void Require(bool condition, string? detail = null)
        {
            if (!condition)
            {
                throw new InvalidDataException("PNG decoding invariant failed. " + detail);
            }
        }
    }
}
