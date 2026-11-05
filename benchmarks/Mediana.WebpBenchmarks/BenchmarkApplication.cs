// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using ManagedWebpEncoder = Mediana.Codecs.Webp.WebpEncoder;
using WebpCompression = Mediana.Codecs.Webp.WebpCompression;
using WebpEncodingOptions = Mediana.Codecs.Webp.WebpEncodingOptions;

namespace Mediana.WebpBenchmarks
{
    internal static class BenchmarkApplication
    {
        internal static async Task RunAsync(string[] args)
        {
            if (args.Length != 6) { throw new ArgumentException("Usage: <encoder-or-verification> <flat|gradient|noise|alpha|image-path> <width> <height> <iterations; 0=auto; lossy export=quality> <output-directory>. See README.md for supported modes."); }
            string mode = args[0];
            string sample = args[1];
            if (mode == "inspect-animation")
            {
                Directory.CreateDirectory(args[5]);
                await BenchmarkAnimationInspection.ReadAsync(sample, args[5]);
                return;
            }
            if (mode == "export-mixed-animation")
            {
                Directory.CreateDirectory(args[5]);
                await BenchmarkAnimation.WriteMixedAsync(args[5]);
                return;
            }
            int width = int.Parse(args[2], CultureInfo.InvariantCulture);
            int height = int.Parse(args[3], CultureInfo.InvariantCulture);
            int iterations = int.Parse(args[4], CultureInfo.InvariantCulture);
            byte[] pixels = BenchmarkPixels.Create(sample, ref width, ref height);
            Directory.CreateDirectory(args[5]);
            MediaBufferAllocator allocator = new(long.MaxValue);
            using WritableMediaBuffer writable = allocator.Allocate(pixels.Length);
            pixels.CopyTo(writable.Memory);
            using MediaBuffer owned = writable.Publish();
            VideoFormat format = new(width, height, VideoPixelFormat.Rgba32, VideoColorInfo.Srgb, SampleAspectRatio.Square);
            using VideoFrame frame = new(format, owned, format.CreateTightLayout());
            Configuration configuration = Configuration.Default.Clone();
            configuration.MaxDegreeOfParallelism = 1;
            using Image<Rgba32> image = Image.LoadPixelData<Rgba32>(configuration, pixels, width, height);
            if (mode is "export-imagesharp-lossy" or "export-imagesharp-lossy-fast")
            {
                WebpEncoder reference = new()
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = iterations,
                    TransparentColorMode = TransparentColorMode.Preserve,
                    Method = mode switch
                    {
                        "export-imagesharp-lossy" => WebpEncodingMethod.Default,
                        "export-imagesharp-lossy-fast" => WebpEncodingMethod.Fastest,
                        _ => throw new ArgumentOutOfRangeException(nameof(mode))
                    }
                };
                await image.SaveAsync(Path.Combine(args[5], "output.webp"), reference);
                await File.WriteAllBytesAsync(Path.Combine(args[5], "input.rgba"), pixels);
                return;
            }
            if (mode is "verify-lossy" or "export-lossy")
            {
                await BenchmarkLossyVerification.WriteAsync(frame, pixels, iterations, allocator, args[5], mode == "verify-lossy");
                return;
            }
            if (mode == "verify-animation")
            {
                await BenchmarkAnimation.VerifyAsync(frame, pixels, allocator, args[5]);
                return;
            }
            if (mode == "verify")
            {
                using MediaBuffer encoded = ManagedWebpEncoder.Encode(frame, allocator);
                await File.WriteAllBytesAsync(Path.Combine(args[5], "output.webp"), encoded.Memory.ToArray());
                await File.WriteAllBytesAsync(Path.Combine(args[5], "input.rgba"), pixels);
                using Image<Rgba32> decoded = Image.Load<Rgba32>(encoded.Memory.Span);
                byte[] actual = new byte[pixels.Length];
                decoded.CopyPixelDataTo(actual);
                if (decoded.Width != width || decoded.Height != height || !actual.AsSpan().SequenceEqual(pixels))
                {
                    int mismatch = actual.AsSpan().CommonPrefixLength(pixels);
                    throw new InvalidDataException("WebP RGBA mismatch at byte " + mismatch);
                }
                Console.WriteLine("PASS exact ImageSharp RGBA: " + sample + " " + width + "x" + height + "; " + encoded.Memory.Length + " encoded bytes.");
                return;
            }
            WebpFileFormatType fileFormat = mode switch
            {
                "mediana" or "imagesharp" or "imagesharp-fast" => WebpFileFormatType.Lossless,
                "mediana-lossy" or "imagesharp-lossy" or "imagesharp-lossy-fast" => WebpFileFormatType.Lossy,
                _ => throw new ArgumentException("Unknown benchmark encoder: " + mode)
            };
            WebpEncodingOptions options = new()
            {
                Compression = fileFormat switch
                {
                    WebpFileFormatType.Lossless => WebpCompression.Lossless,
                    WebpFileFormatType.Lossy => WebpCompression.Lossy,
                    _ => throw new ArgumentOutOfRangeException(nameof(fileFormat))
                },
                Quality = 75
            };
            WebpEncoder encoder = new()
            {
                FileFormat = fileFormat,
                Method = mode switch
                {
                    "imagesharp-fast" or "imagesharp-lossy-fast" => WebpEncodingMethod.Fastest,
                    "mediana" or "imagesharp" or "mediana-lossy" or "imagesharp-lossy" => WebpEncodingMethod.Default,
                    _ => throw new ArgumentException("Unknown benchmark encoder: " + mode)
                },
                Quality = 75,
                NearLossless = false,
                TransparentColorMode = TransparentColorMode.Preserve
            };
            long Encode()
            {
                switch (mode)
                {
                    case "mediana":
                    case "mediana-lossy":
                        using (MediaBuffer encoded = ManagedWebpEncoder.Encode(frame, allocator, options)) { return encoded.Memory.Length; }
                    case "imagesharp":
                    case "imagesharp-fast":
                    case "imagesharp-lossy":
                    case "imagesharp-lossy-fast":
                        using (CountingWriteStream stream = new()) { image.Save(stream, encoder); return stream.Written; }
                    default: throw new ArgumentException("Unknown benchmark encoder: " + mode);
                }
            }
            Stopwatch warmup = Stopwatch.StartNew();
            long size = Encode();
            int warmupCount = 1;
            do { Encode(); warmupCount++; } while (warmupCount < 3 || warmup.ElapsedMilliseconds < 500);
            warmup.Stop();
            if (iterations == 0) { iterations = Math.Clamp((int)(3000 / Math.Max(1, warmup.Elapsed.TotalMilliseconds / warmupCount)), 3, 1000); }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using Process process = Process.GetCurrentProcess();
            long allocated = GC.GetTotalAllocatedBytes(true);
            TimeSpan cpu = process.TotalProcessorTime;
            Stopwatch wall = Stopwatch.StartNew();
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                long current = Encode();
                if (current != size) { throw new InvalidDataException("Encoder output length is not deterministic."); }
            }
            wall.Stop();
            TimeSpan elapsedCpu = process.TotalProcessorTime - cpu;
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            process.Refresh();
            BenchmarkResult result = new(mode, Path.GetFileName(sample), width, height, iterations,
                wall.Elapsed.TotalMilliseconds / iterations, elapsedCpu.TotalMilliseconds / iterations,
                allocated / iterations, process.PeakWorkingSet64, size);
            Console.WriteLine(JsonSerializer.Serialize(result, BenchmarkJsonContext.Default.BenchmarkResult));
        }
    }
}
