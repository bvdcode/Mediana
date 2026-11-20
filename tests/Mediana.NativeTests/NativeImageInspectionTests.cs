// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Images.Metadata;
using Mediana.Images.Heif;

namespace Mediana.NativeTests
{
    internal static class NativeImageInspectionTests
    {
        internal static async Task RunAsync(string root)
        {
            byte[] jpeg = await File.ReadAllBytesAsync(Path.Combine(root, "metadata-preview.jpg"));
            MediaEngine engine = new MediaEngineBuilder().AddImageInspector(new TiffImageMetadataReader())
                .AddImageInspector(new RafMetadataReader()).AddImageInspector(new PcxMetadataReader())
                .AddImageInspector(new PsdMetadataReader()).Build();
            foreach (string family in new[] { "nrw", "orf", "rw2", "pef", "srw" })
            {
                foreach (string order in new[] { "le", "be" })
                {
                    byte[] encoded = await File.ReadAllBytesAsync(Path.Combine(root, $"metadata-sensor-{family}-{order}.{family}"));
                    Core.Metadata.ImageInfo info = await engine.InspectImageAsync(encoded);
                    IReadOnlyList<EncodedImagePreview> previews = await engine.ReadImagePreviewsAsync(encoded);
                    if (info.FormatId != family || info.Width != 3024 || info.Height != 4032 || previews.Count != 1
                        || !previews[0].Encoded.Span.SequenceEqual(jpeg))
                    { throw new InvalidDataException("RAW inspection differs from its declared sensor and original preview."); }
                }
            }
            byte[] raf = await File.ReadAllBytesAsync(Path.Combine(root, "metadata-sensor.raf"));
            ImageInspection result = new RafMetadataReader().Inspect(raf);
            if (result.Info.Width != 4032 || result.Info.Height != 3024 || result.Previews.Count != 1
                || !result.Previews[0].Encoded.Span.SequenceEqual(jpeg))
            { throw new InvalidDataException("RAF inspection differs from its declared sensor and original preview."); }
            foreach (string extension in new[] { "pcx", "psd", "psb" })
            {
                byte[] file = await File.ReadAllBytesAsync(Path.Combine(root, "metadata-reference." + extension));
                Core.Metadata.ImageInfo info = await engine.InspectImageAsync(file);
                if (info.Width != 37 || info.Height != 29) { throw new InvalidDataException("Image inspection dimensions differ."); }
            }
            byte[] avif = await File.ReadAllBytesAsync(Path.Combine(root, "metadata-oriented-thumbnail.avif"));
            AvifImageInspector avifReader = new(new(maxPixels: 1));
            Core.Metadata.ImageInfo avifInfo = await avifReader.ReadInfoAsync(avif);
            IReadOnlyList<EncodedImagePreview> thumbnails = await avifReader.ReadPreviewsAsync(avif);
            if (avifInfo.Width != 29 || avifInfo.Height != 37 || thumbnails.Count != 1 || thumbnails[0].Info.FormatId != "av1")
            { throw new InvalidDataException("AVIF transforms or thumbnail differ."); }
            Console.WriteLine("PASS PCX, PSD/PSB, TIFF RAW and RAF metadata and original previews.");
        }
    }
}
