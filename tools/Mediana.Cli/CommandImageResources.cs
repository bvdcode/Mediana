// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Processing;
using Mediana.Documents.OpenXml;
using Mediana.Documents.Pdf;
using Mediana.Drawing;

namespace Mediana.Cli
{
    internal static class CommandImageResources
    {
        internal static int MaximumBufferLength => Array.MaxLength;
        internal static int MaximumRgbaPixels => Array.MaxLength / 4;
        internal static ImageOpenOptions OpenOptions { get; } = new() { MaxInputBytes = Array.MaxLength };
        internal static VisualExportOptions ExportOptions { get; } = new()
        {
            ColorResolver = PreviewColorPolicy.Resolve,
            ToneMapping = new(),
            UnspecifiedSampleAspectRatio = SampleAspectRatio.Square
        };
        internal static MediaOpenOptions ProbeOptions => CommandMediaResources.OpenOptions;
        internal static OpenXmlPackageOptions PackageOptions { get; } = new()
        {
            MaximumInputBytes = Array.MaxLength,
            MaximumPartBytes = Array.MaxLength,
            MaximumUncompressedBytes = long.MaxValue
        };
        internal static PdfReaderOptions PdfReaderOptions { get; } = new()
        {
            MaximumInputBytes = Array.MaxLength,
            MaximumDecodedBytes = Array.MaxLength,
            MaximumImageBytes = Array.MaxLength,
            MaximumImagePixels = Array.MaxLength / 4
        };

        internal static MediaBufferAllocator CreateAllocator() => CommandMediaResources.CreateAllocator();

        internal static DrawingRenderOptions DrawingOptions(int? width = null) => new()
        {
            Width = width,
            MaximumSamples = Array.MaxLength / 4,
            MaximumEffectBytes = long.MaxValue
        };

        internal static PdfRenderOptions PdfRenderOptions { get; } = new()
        {
            MaximumSamples = Array.MaxLength / 4,
            MaximumWorkingBytes = long.MaxValue
        };
    }
}
