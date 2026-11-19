// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Mediana.Codecs;
using Mediana.Codecs.Png;
using Mediana.Codecs.Tiff;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;

namespace Mediana.CompressionBenchmarks
{
    internal static class CompressionBenchmark
    {
        internal static async Task RunAsync(string[] args)
        {
            if (args.Length != 3)
            {
                throw new ArgumentException("Use <png|png-stream|tiff|pdf> <input-file> <iterations>.");
            }
            byte[] encoded = await File.ReadAllBytesAsync(args[1]);
            int iterations = int.Parse(args[2], CultureInfo.InvariantCulture);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);
            MediaBufferAllocator allocator = new(long.MaxValue);
            ImageDecodeRequest request = new();
            TiffImageDecoder decoder = new();
            using VideoFrame? source = args[0] is "png" or "png-stream" ? await decoder.DecodeAsync(encoded, request, allocator) : null;
            long initialBytes = allocator.AllocatedBytes;
            string hash = string.Empty;
            bool capture = true;
            async Task OnceAsync()
            {
                switch (args[0])
                {
                    case "png":
                        using (MediaBuffer result = PngEncoder.Encode(source!, allocator))
                        {
                            if (capture) { hash = Convert.ToHexString(SHA256.HashData(result.Memory.Span)); }
                        }
                        break;
                    case "png-stream":
                        if (capture)
                        {
                            using MemoryStream result = new();
                            await PngEncoder.EncodeAsync(source!, result, allocator);
                            hash = Convert.ToHexString(SHA256.HashData(result.GetBuffer().AsSpan(0, checked((int)result.Length))));
                        }
                        else { await PngEncoder.EncodeAsync(source!, Stream.Null, allocator); }
                        break;
                    case "tiff":
                        using (VideoFrame result = await decoder.DecodeAsync(encoded, request, allocator))
                        {
                            if (capture) { hash = Convert.ToHexString(SHA256.HashData(result.Data.Span)); }
                        }
                        break;
                    case "pdf":
                        using (PdfDocument document = PdfReader.Open(encoded))
                        using (PdfPage page = document.GetPage(1))
                        {
                            if (capture)
                            {
                                using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                                foreach (PdfImage image in page.Images) { digest.AppendData(image.Pixels.Span); }
                                hash = Convert.ToHexString(digest.GetHashAndReset());
                            }
                        }
                        break;
                    default: throw new ArgumentException("Unknown compression workload.");
                }
            }
            await OnceAsync();
            capture = false;
            Stopwatch warmup = Stopwatch.StartNew();
            do { await OnceAsync(); } while (warmup.ElapsedMilliseconds < 500);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using Process process = Process.GetCurrentProcess();
            TimeSpan cpu = process.TotalProcessorTime;
            long allocated = GC.GetTotalAllocatedBytes(true);
            Stopwatch elapsed = Stopwatch.StartNew();
            for (int index = 0; index < iterations; index++) { await OnceAsync(); }
            elapsed.Stop();
            cpu = process.TotalProcessorTime - cpu;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            if (allocator.AllocatedBytes != initialBytes) { throw new InvalidDataException("The operation retained media buffers."); }
            process.Refresh();
            Console.WriteLine(FormattableString.Invariant(
                $"operation={args[0]}; iterations={iterations}; elapsed-ms={elapsed.Elapsed.TotalMilliseconds / iterations:F6}; cpu-ms={cpu.TotalMilliseconds / iterations:F6}; allocated-bytes={allocated / iterations}; peak-rss-bytes={process.PeakWorkingSet64}; sha256={hash}"));
        }
    }
}
