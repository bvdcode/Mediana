// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Tests
{
    internal static class PdfPageGroupTestInput
    {
        internal static byte[] Create(string group) => PdfTestDocument.Create(
            "/S gs 1 0 0 rg 10 10 20 20 re f", pageEntries: group,
            resources: "/ExtGState << /S << /BM /Screen /ca 0.5 >> >>");

        internal static async Task VerifyAsync()
        {
            foreach (string group in new[] { "", "/Group << /S /Transparency /CS /DeviceRGB /I false /K false >>" })
            {
                using PdfDocument document = await PdfReader.ReadAsync(new MemoryStream(Create(group)));
                using PixelBuffer image = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1 });
                using PixelBuffer transparent = PdfRenderer.Render(document, 1, new() { Dpi = 72, Supersampling = 1, Background = default });
                if (image[20, 80] != new Rgba32(255, 127, 127) || image[40, 80] != new Rgba32(255, 255, 255)
                    || transparent[20, 80] != new Rgba32(255, 0, 0, 128) || transparent[40, 80].A != 0)
                {
                    throw new InvalidDataException("The PDF page group must blend over transparency before compositing its backdrop.");
                }
            }
        }
    }
}
