// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg2000;
using Mediana.Containers.Mp4;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Images.Heif;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;
using Mediana.IO;

namespace Mediana.Tests
{
    internal static class ImageProbeTestInput
    {
        public static async Task VerifyAsync(string root)
        {
            MediaEngine engine = new MediaEngineBuilder().AddContainer(new Mp4ReaderFactory())
                .AddImageDecoder(new SvgImageDecoder()).AddImageDecoder(new TgsImageDecoder())
                .AddImageDecoder(new PdfImageDecoder()).AddImageDecoder(new HeicImageDecoder(SampleAspectRatio.Square))
                .AddImageDecoder(new Jpeg2000ImageDecoder(SampleAspectRatio.Square))
                .AddImageDecoder(new Jbig2ImageDecoder(SampleAspectRatio.Square)).Build();
            (string Folder, string File, string Format, double Width, double Height, ImageDimensionUnit Unit, int? Pages)[] cases =
            [
                ("Svg", "vector-preview.svg", "svg", 800, 480, ImageDimensionUnit.Pixels, null),
                ("Tgs", "geometry.tgs", "tgs", 400, 320, ImageDimensionUnit.Pixels, null),
                ("Pdf", "graphics.pdf", "pdf", 200, 200, ImageDimensionUnit.Points, 2),
                ("Heif", "rotate-90.heic", "heif", 48, 64, ImageDimensionUnit.Pixels, null),
                ("Jpeg2000", "RGB-129x101.jp2", "jpeg2000", 129, 101, ImageDimensionUnit.Pixels, null),
                ("Jbig2", "mmr-white.jb2", "jbig2", 173, 9, ImageDimensionUnit.Pixels, 1)
            ];
            foreach ((string folder, string file, string format, double width, double height, ImageDimensionUnit unit, int? pages) in cases)
            {
                await using FileStream stream = File.OpenRead(Path.Combine(root, folder, file));
                await using RandomAccessStreamByteSource source = new(stream, leaveOpen: true);
                await source.ReadAsync(new byte[3]);
                MediaInfo result = await engine.ProbeAsync(source);
                ImageInfo info = result.Image ?? throw new InvalidDataException("Image metadata is missing.");
                if (result.Container is not null || result.Streams.Count != 0 || result.StreamMetadata.Count != 0 ||
                    info.FormatId != format || info.Width != width || info.Height != height || info.DimensionUnit != unit ||
                    info.PageCount != pages || stream.Position != 3 || !stream.CanRead)
                {
                    throw new InvalidDataException("Image metadata or borrowed source state differs: " + file);
                }
            }
        }
    }
}
