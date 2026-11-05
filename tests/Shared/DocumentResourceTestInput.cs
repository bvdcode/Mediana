// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
using Mediana.Codecs;
using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Pdf;
using Mediana.Documents.Word;
using Mediana.Drawing;
using Mediana.Drawing.Fonts;
using Mediana.Images.Svg;

namespace Mediana.Tests
{
    internal static class DocumentResourceTestInput
    {
        internal static async Task VerifyAsync()
        {
            int resolutions = 0;
            TextOptions text = new() { FontProgramResolver = _ => { resolutions++; return BundledFontPrograms.GetProgram("Canada1500"); } };
            int loads = 0;
            MediaResourceSet resources = MediaResourceSet.Empty.WithAsync<TextOptions>(async token =>
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                loads++;
                return text;
            });
            MediaEngine engine = new MediaEngineBuilder().UseResources(resources)
                .AddImageDecoder(new SvgImageDecoder(renderOptions: new() { Supersampling = 1 }))
                .AddImageDecoder(new PdfImageDecoder(renderOptions: new() { Dpi = 72, Supersampling = 1 }))
                .AddImageDecoder(new OpenXmlImageDecoder([new WordPageDecoder(renderOptions: new() { Dpi = 72, Supersampling = 1 })])).Build();
            byte[] svg = Encoding.UTF8.GetBytes("<svg width='32' height='32'><text y='20' font-family='Shared'>A</text></svg>");
            byte[] word = WordTestInput.Create(WordTestInput.PictureBody, WordRenderTestInput.Fonts, page: WordRenderTestInput.SmallPage, picture: svg);
            byte[] pdf = PdfTestDocument.Create("BT /F1 12 Tf 10 40 Td (A) Tj ET", resources: "/Font << /F1 5 0 R >>",
                extraObjects: PdfTestDocument.Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
            if (engine.RecognizeImage(svg, svg.Length) is null || loads != 0) { throw new InvalidDataException("Probing resolved text resources."); }
            MediaBufferAllocator allocator = new(2_000_000);
            foreach (byte[] encoded in new[] { EmbeddedImageTestInput.Svg(svg), word, pdf })
            {
                int previous = resolutions;
                using VideoFrame frame = await engine.DecodeImageAsync(encoded, allocator);
                if (resolutions == previous) { throw new InvalidDataException("A document did not use the shared font program."); }
            }
            if (loads < 5 || allocator.AllocatedBytes != 0) { throw new InvalidDataException("Nested document resources or image ownership were lost."); }
            TextOptions invalid = new() { FontProgramResolver = _ => throw new InvalidDataException("request-font") };
            bool rejected = false;
            try
            {
                using VideoFrame frame = await engine.DecodeImageAsync(svg, allocator,
                    new ImageDecodeRequest { Resources = MediaResourceSet.Empty.With(invalid) });
            }
            catch (InvalidDataException error) when (error.Message == "request-font") { rejected = true; }
            if (!rejected || allocator.AllocatedBytes != 0) { throw new InvalidDataException("Request resources did not override engine resources safely."); }
        }
    }
}
