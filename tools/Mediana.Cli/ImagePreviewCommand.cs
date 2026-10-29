// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg2000;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Images.Heif;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;
using Mediana.Processing;

namespace Mediana.Cli
{
    internal static class ImagePreviewCommand
    {
        private const int MaximumInputBytes = 64 * 1024 * 1024;
        private const int MaximumPixels = 16 * 1024 * 1024;
        private const long BufferBudgetBytes = 256L * 1024 * 1024;

        public static async Task<int> RasterAsync(string kind, string inputPath, string outputPath, SampleAspectRatio aspect, CancellationToken cancellationToken = default)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            using (VideoFrame input = await ReadRasterAsync(kind, inputPath, aspect, allocator, cancellationToken))
            {
                await SaveAsync(input, outputPath, allocator, cancellationToken);
            }
            VerifyReleased(allocator);
            return 0;
        }

        public static async Task<int> SvgAsync(string inputPath, string outputPath, int width, CancellationToken cancellationToken = default)
        {
            SvgDocument document = await SvgReader.ReadFileAsync(inputPath, cancellationToken: cancellationToken);
            PixelBuffer image = SvgRenderer.Render(document, new SvgRenderOptions { Width = width }, cancellationToken: cancellationToken);
            return await SaveDrawingAsync(image, outputPath, cancellationToken);
        }

        public static async Task<int> TgsAsync(string inputPath, string outputPath, double seconds, int width, CancellationToken cancellationToken = default)
        {
            TgsDocument document = await TgsReader.ReadFileAsync(inputPath, cancellationToken: cancellationToken);
            PixelBuffer image = TgsRenderer.RenderAtTime(document, seconds, new SvgRenderOptions { Width = width }, cancellationToken: cancellationToken);
            return await SaveDrawingAsync(image, outputPath, cancellationToken);
        }

        private static async Task<int> SaveDrawingAsync(PixelBuffer image, string outputPath, CancellationToken cancellationToken)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            using (VideoFrame frame = image.ToVideoFrame(allocator, cancellationToken))
            {
                await SaveAsync(frame, outputPath, allocator, cancellationToken);
            }
            VerifyReleased(allocator);
            return 0;
        }

        private static async Task<VideoFrame> ReadRasterAsync(string kind, string path, SampleAspectRatio aspect,
            MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            switch (kind)
            {
                case "heic":
                    await using (FileStream stream = File.OpenRead(path))
                    {
                        HeicDocument document = await HeicDocument.LoadAsync(stream,
                            new HeicDecodeOptions(maxInputBytes: MaximumInputBytes, maxPixels: MaximumPixels), cancellationToken: cancellationToken);
                        RgbaImage image = await document.DecodePrimaryAsync(cancellationToken);
                        return image.ToVideoFrame(allocator, aspect, cancellationToken: cancellationToken);
                    }
                case "jp2":
                    Jpeg2000Image jpeg = Jpeg2000Decoder.Decode(await ReadBoundedAsync(path, cancellationToken), new()
                    {
                        MaximumEncodedBytes = MaximumInputBytes,
                        MaximumPixels = MaximumPixels
                    }, cancellationToken: cancellationToken);
                    return jpeg.ToVideoFrame(allocator, aspect, cancellationToken: cancellationToken);
                case "jbig2":
                    Jbig2Document jbig = Jbig2Decoder.Decode(await ReadBoundedAsync(path, cancellationToken), options: new()
                    {
                        MaximumEncodedBytes = MaximumInputBytes,
                        MaximumPixels = MaximumPixels
                    }, cancellationToken: cancellationToken);
                    if (jbig.Pages.Count == 0)
                    {
                        throw new InvalidDataException("The JBIG2 document has no page to preview.");
                    }
                    return jbig.Pages[0].ToVideoFrame(allocator, aspect, cancellationToken: cancellationToken);
                default:
                    throw new ArgumentException("Image kind must be heic, jp2 or jbig2.", nameof(kind));
            }
        }

        private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
        {
            await using FileStream stream = File.OpenRead(path);
            if (stream.Length > MaximumInputBytes)
            {
                throw new InvalidDataException("Encoded image exceeds the input byte limit.");
            }
            byte[] input = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(input, cancellationToken);
            return input;
        }

        private static async Task SaveAsync(VideoFrame frame, string outputPath, MediaBufferAllocator allocator, CancellationToken cancellationToken)
        {
            using VideoFrame srgb = SrgbConverter.Apply(frame, allocator, cancellationToken: cancellationToken);
            using MediaBuffer png = PngEncoder.Encode(srgb, allocator, cancellationToken: cancellationToken);
            await CommandOutputFile.WriteAsync(outputPath, async output => await output.WriteAsync(png.Memory, cancellationToken), cancellationToken: cancellationToken);
            await Console.Out.WriteLineAsync($"PNG image: {frame.Format.Width}x{frame.Format.Height}; {png.Memory.Length} bytes; sRGB.");
        }

        private static void VerifyReleased(MediaBufferAllocator allocator)
        {
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("Image output buffers were not released.");
            }
        }
    }
}
