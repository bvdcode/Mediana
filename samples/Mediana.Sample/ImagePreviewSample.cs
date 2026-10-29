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

namespace Mediana.Sample
{
    internal static class ImagePreviewSample
    {
        private const int MaximumInputBytes = 64 * 1024 * 1024;
        private const int MaximumPixels = 16 * 1024 * 1024;
        private const long BufferBudgetBytes = 256L * 1024 * 1024;

        public static async Task<int> RasterAsync(string kind, string inputPath, string outputPath, SampleAspectRatio aspect)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            using (VideoFrame input = await ReadRasterAsync(kind, inputPath, aspect, allocator))
            {
                await SaveAsync(input, outputPath, allocator);
            }
            VerifyReleased(allocator);
            return 0;
        }

        public static async Task<int> SvgAsync(string inputPath, string outputPath, int width)
        {
            SvgDocument document = await SvgReader.ReadFileAsync(inputPath);
            PixelBuffer image = SvgRenderer.Render(document, new SvgRenderOptions { Width = width });
            return await SaveDrawingAsync(image, outputPath);
        }

        public static async Task<int> TgsAsync(string inputPath, string outputPath, double seconds, int width)
        {
            TgsDocument document = await TgsReader.ReadFileAsync(inputPath);
            PixelBuffer image = TgsRenderer.RenderAtTime(document, seconds, new SvgRenderOptions { Width = width });
            return await SaveDrawingAsync(image, outputPath);
        }

        private static async Task<int> SaveDrawingAsync(PixelBuffer image, string outputPath)
        {
            MediaBufferAllocator allocator = new(BufferBudgetBytes);
            using (VideoFrame frame = image.ToVideoFrame(allocator))
            {
                await SaveAsync(frame, outputPath, allocator);
            }
            VerifyReleased(allocator);
            return 0;
        }

        private static async Task<VideoFrame> ReadRasterAsync(string kind, string path, SampleAspectRatio aspect,
            MediaBufferAllocator allocator)
        {
            switch (kind)
            {
                case "heic":
                    await using (FileStream stream = File.OpenRead(path))
                    {
                        HeicDocument document = await HeicDocument.LoadAsync(stream,
                            new HeicDecodeOptions(maxInputBytes: MaximumInputBytes, maxPixels: MaximumPixels));
                        RgbaImage image = await document.DecodePrimaryAsync();
                        return image.ToVideoFrame(allocator, aspect);
                    }
                case "jp2":
                    Jpeg2000Image jpeg = Jpeg2000Decoder.Decode(await ReadBoundedAsync(path), new()
                    {
                        MaximumEncodedBytes = MaximumInputBytes,
                        MaximumPixels = MaximumPixels
                    });
                    return jpeg.ToVideoFrame(allocator, aspect);
                case "jbig2":
                    Jbig2Document jbig = Jbig2Decoder.Decode(await ReadBoundedAsync(path), options: new()
                    {
                        MaximumEncodedBytes = MaximumInputBytes,
                        MaximumPixels = MaximumPixels
                    });
                    if (jbig.Pages.Count == 0)
                    {
                        throw new InvalidDataException("The JBIG2 document has no page to preview.");
                    }
                    return jbig.Pages[0].ToVideoFrame(allocator, aspect);
                default:
                    throw new ArgumentException("Image kind must be heic, jp2 or jbig2.", nameof(kind));
            }
        }

        private static async Task<byte[]> ReadBoundedAsync(string path)
        {
            await using FileStream stream = File.OpenRead(path);
            if (stream.Length > MaximumInputBytes)
            {
                throw new InvalidDataException("Encoded image exceeds the input byte limit.");
            }
            byte[] input = new byte[checked((int)stream.Length)];
            await stream.ReadExactlyAsync(input);
            return input;
        }

        private static async Task SaveAsync(VideoFrame frame, string outputPath, MediaBufferAllocator allocator)
        {
            using VideoFrame srgb = SrgbConverter.Apply(frame, allocator);
            using MediaBuffer png = PngEncoder.Encode(srgb, allocator);
            await SampleOutputFile.WriteAsync(outputPath, async output => await output.WriteAsync(png.Memory));
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
