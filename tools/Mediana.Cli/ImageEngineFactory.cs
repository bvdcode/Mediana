// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Jpeg2000;
using Mediana.Codecs.Png;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Images.Heif;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;

namespace Mediana.Cli
{
    internal static class ImageEngineFactory
    {
        private const int MaximumInputBytes = 64 * 1024 * 1024;
        private const int MaximumPixels = 16 * 1024 * 1024;
        private const int MaximumDocumentPreviewDimension = 1200;

        public static MediaEngine Create() => AddDecoders(new MediaEngineBuilder()).Build();

        internal static MediaEngineBuilder AddDecoders(MediaEngineBuilder builder) => AddStillImageDecoders(builder)
            .AddImageDecoder(CreateJbig2(SampleAspectRatio.Square))
            .AddImageDecoder(new TgsImageDecoder())
            .AddImageDecoder(new PdfImageDecoder(renderOptions: new()
            {
                MaximumWidth = MaximumDocumentPreviewDimension,
                MaximumHeight = MaximumDocumentPreviewDimension
            }));

        internal static MediaEngineBuilder AddStillImageDecoders(MediaEngineBuilder builder) => builder
            .AddImageDecoder(new PngImageDecoder())
            .AddImageDecoder(new JpegImageDecoder())
            .AddImageDecoder(CreateHeic(SampleAspectRatio.Square))
            .AddImageDecoder(CreateJpeg2000(SampleAspectRatio.Square))
            .AddImageDecoder(new SvgImageDecoder());

        public static HeicImageDecoder CreateHeic(SampleAspectRatio aspect) => new(aspect,
            new HeicDecodeOptions(maxInputBytes: MaximumInputBytes, maxPixels: MaximumPixels));
        public static Jpeg2000ImageDecoder CreateJpeg2000(SampleAspectRatio aspect) => new(aspect, new()
        {
            MaximumEncodedBytes = MaximumInputBytes,
            MaximumPixels = MaximumPixels
        });
        public static Jbig2ImageDecoder CreateJbig2(SampleAspectRatio aspect) => new(aspect, new()
        {
            MaximumEncodedBytes = MaximumInputBytes,
            MaximumPixels = MaximumPixels
        });
    }
}
