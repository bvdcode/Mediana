// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Timing;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class VectorImageEngineTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            MediaEngine svgEngine = CreateEngine(new SvgRenderOptions { Supersampling = 1 });
            MediaBufferAllocator allocator = new(4 * 1024 * 1024);
            using (JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Svg", "filter-reference.json"))))
            {
                int compared = 0;
                foreach (JsonElement entry in manifest.RootElement.GetProperty("Cases").EnumerateArray())
                {
                    byte[] encoded = Encoding.UTF8.GetBytes(entry.GetProperty("Svg").GetString()!);
                    using (VideoFrame frame = await DecodeAsync(svgEngine, encoded, allocator))
                    {
                        string actual = Convert.ToHexStringLower(SHA256.HashData(frame.Data.Span));
                        if (actual != entry.GetProperty("Sha256").GetString())
                        {
                            throw new InvalidDataException("Registered SVG pixels differ from the independent filter reference: " + entry.GetProperty("Name").GetString());
                        }
                    }
                    VerifyReleased(allocator);
                    compared++;
                }
                if (compared != 352)
                {
                    throw new InvalidDataException("The registered SVG corpus must compare 352 independent cases.");
                }
            }
            using (JsonDocument manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(root, "Tgs", "render-reference.json"))))
            {
                int compared = 0;
                foreach (JsonElement entry in manifest.RootElement.EnumerateArray())
                {
                    string name = entry.GetProperty("file").GetString()!;
                    if (TgsReferenceTestInput.ContainsExpressions(name))
                    {
                        continue;
                    }
                    byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, "Tgs", name));
                    TgsDocument document = TgsReader.Read(encoded);
                    int size = entry.GetProperty("size").GetInt32();
                    MediaTimestamp time = new(TimeSpan.FromSeconds((entry.GetProperty("frame").GetDouble() - document.InPoint) / document.FrameRate).Ticks,
                        TimeBase.TimeSpanTicks);
                    MediaEngine engine = CreateEngine(new SvgRenderOptions { Width = size, Height = size, Supersampling = 1 });
                    using (VideoFrame frame = await DecodeAsync(engine, encoded, allocator, new ImageDecodeRequest { Time = time }))
                    {
                        byte[] reference = new byte[checked(size * size * 4)];
                        await using FileStream compressed = File.OpenRead(Path.Combine(root, "Tgs", entry.GetProperty("pixels").GetString()!));
                        await using GZipStream pixels = new(compressed, CompressionMode.Decompress);
                        await pixels.ReadExactlyAsync(reference);
                        if (frame.Format.Width != size || frame.Format.Height != size || frame.Timestamp != time || frame.Duration is null
                            || Convert.ToHexStringLower(SHA256.HashData(reference)) != entry.GetProperty("sha256").GetString()
                            || await pixels.ReadAsync(new byte[1]) != 0)
                        {
                            throw new InvalidDataException("Registered TGS dimensions, timing or independent reference differ: " + name);
                        }
                        TgsReferenceTestInput.VerifyPixels(frame.Data.Span, reference, name);
                    }
                    VerifyReleased(allocator);
                    compared++;
                }
                if (compared != 33)
                {
                    throw new InvalidDataException("The registered TGS corpus must compare 33 supported independent frames.");
                }
            }
            MediaEngine pdfEngine = CreateEngine();
            foreach (string name in new[] { "graphics", "report" })
            {
                byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, "Pdf", name + ".pdf"));
                using (VideoFrame frame = await DecodeAsync(pdfEngine, encoded, allocator))
                {
                    await PdfPixelReference.VerifyAsync(Path.Combine(root, "Pdf"), name, frame.Format.Width, frame.Format.Height, frame.Data);
                }
                VerifyReleased(allocator);
            }
            byte[] graphics = await File.ReadAllBytesAsync(Path.Combine(root, "Pdf", "graphics.pdf"));
            MediaEngine pageEngine = CreateEngine(pdfOptions: new PdfRenderOptions { Dpi = 72, Supersampling = 1 });
            using (VideoFrame frame = await DecodeAsync(pageEngine, graphics, allocator, new ImageDecodeRequest { PageIndex = 1 }))
            {
                ReadOnlySpan<byte> purple = frame.GetRow(0, 30).Slice(30 * 4, 4);
                ReadOnlySpan<byte> blue = frame.GetRow(0, 30).Slice(170 * 4, 4);
                if (Math.Abs(purple[0] - 128) > 1 || Math.Abs(purple[2] - 128) > 1
                    || !blue.SequenceEqual(new byte[] { 0, 0, 255, 255 }))
                {
                    throw new InvalidDataException("Registered PDF page selection or transparency differs from the authored second-page pixels.");
                }
            }
            VerifyReleased(allocator);
        }

        private static MediaEngine CreateEngine(SvgRenderOptions? vectorOptions = null, PdfRenderOptions? pdfOptions = null) =>
            new MediaEngineBuilder().AddImageDecoder(new SvgImageDecoder(renderOptions: vectorOptions))
                .AddImageDecoder(new TgsImageDecoder(renderOptions: vectorOptions))
                .AddImageDecoder(new PdfImageDecoder(renderOptions: pdfOptions)).Build();

        private static async Task<VideoFrame> DecodeAsync(MediaEngine engine, byte[] encoded, MediaBufferAllocator allocator,
            ImageDecodeRequest? request = null)
        {
            using MemoryStream memory = new(encoded);
            await using RandomAccessStreamByteSource origin = new(memory, leaveOpen: true);
            memory.Position = 3;
            VideoFrame frame = await engine.DecodeImageAsync(origin, allocator, request);
            bool transferred = false;
            try
            {
                if (memory.Position != 3 || !memory.CanRead || frame.Format.PixelFormat != VideoPixelFormat.Rgba32
                    || frame.Format.Color != VideoColorInfo.Srgb || frame.Format.SampleAspectRatio != SampleAspectRatio.Square
                    || allocator.AllocatedBytes != (long)frame.Format.Width * frame.Format.Height * 4)
                {
                    throw new InvalidDataException("Registered vector/document frame ownership, source cursor or color metadata differ.");
                }
                transferred = true;
                return frame;
            }
            finally
            {
                if (!transferred)
                {
                    frame.Dispose();
                }
            }
        }

        private static void VerifyReleased(MediaBufferAllocator allocator)
        {
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Registered vector/document decoding retained media buffers.");
            }
        }
    }
}
