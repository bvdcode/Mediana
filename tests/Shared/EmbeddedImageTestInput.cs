// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Codecs;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Png;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Images.Svg;
using Mediana.Processing;

namespace Mediana.Tests
{
    internal static class EmbeddedImageTestInput
    {
        internal static async Task VerifyRegistryAsync()
        {
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new EmbeddedTestImageDecoder())
                .AddImageDecoder(new SvgImageDecoder(renderOptions: new() { Supersampling = 1 }))
                .AddImageDecoder(new OpenXmlImageDecoder([new WordPageDecoder(renderOptions: new() { Dpi = 72, Supersampling = 1 })])).Build();
            MediaBufferAllocator allocator = new(1_000_000);
            byte[] picture = "PIX"u8.ToArray();
            using (VideoFrame svg = await engine.DecodeImageAsync(Svg(picture), allocator))
            { Require(svg.Data.Span.SequenceEqual(new byte[] { 21, 43, 65, 255 })); }
            byte[] word = WordTestInput.Create(WordTestInput.PictureBody, WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage, picture: picture);
            using (VideoFrame page = await engine.DecodeImageAsync(word, allocator))
            { Require(page.GetRow(0, 12).Slice(12 * 4, 4).SequenceEqual(new byte[] { 21, 43, 65, 255 })); }
            Require(allocator.AllocatedBytes == 0);
        }

        internal static async Task VerifyColorAsync()
        {
            byte[] png = PngDecodeTestData.Image([0, 128, 128, 128, 255], PngDecodeTestData.Chunk("cICP", [1, 8, 0, 1]));
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new PngImageDecoder())
                .AddImageDecoder(new SvgImageDecoder(renderOptions: new() { Supersampling = 1 })).Build();
            MediaBufferAllocator allocator = new(64);
            using (VideoFrame original = await engine.DecodeImageAsync(png, allocator))
            using (VideoFrame reference = SrgbConverter.Apply(original, allocator))
            using (VideoFrame embedded = await engine.DecodeImageAsync(Svg(png), allocator))
            { Require(reference.Data.Span.SequenceEqual(new byte[] { 188, 188, 188, 255 })); Require(reference.Data.Span.SequenceEqual(embedded.Data.Span)); }
            Require(allocator.AllocatedBytes == 0);
        }

        internal static async Task VerifyJpegAsync(string root)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(root, "rgb-s2-1x1.jpg"));
            MediaBufferAllocator allocator = new(1_000_000);
            SvgReaderOptions options = new() { ImageColorResolver = SdrPreviewColorPolicy.Resolve };
            MediaEngine engine = new MediaEngineBuilder().AddImageDecoder(new JpegImageDecoder())
                .AddImageDecoder(new SvgImageDecoder(options, new() { Supersampling = 1 })).Build();
            using (VideoFrame original = await engine.DecodeImageAsync(jpeg, allocator))
            using (VideoFrame reference = SrgbConverter.Apply(original, SdrPreviewColorPolicy.Resolve(original.Format), allocator))
            using (VideoFrame embedded = await engine.DecodeImageAsync(Svg(jpeg, "image/jpeg"), allocator))
            { Require(reference.Data.Span.SequenceEqual(embedded.Data.Span)); }
            using SvgDocument synchronous = SvgReader.Read(Encoding.UTF8.GetString(Svg(jpeg, "image/jpeg")), options);
            using PixelBuffer pixels = SvgRenderer.Render(synchronous, new() { Supersampling = 1 });
            Require(pixels.Width == 1 && pixels.Height == 1);
            Require(allocator.AllocatedBytes == 0);
        }

        internal static byte[] Svg(byte[] image, string mediaType = "application/octet-stream") =>
            Encoding.UTF8.GetBytes("<svg width='1' height='1'><image width='1' height='1' href='data:" + mediaType + ";base64," + Convert.ToBase64String(image) + "'/></svg>");

        private static void Require(bool condition)
        {
            if (!condition) { throw new InvalidDataException("Embedded image registration, color or ownership differs from the standalone image."); }
        }
    }
}
