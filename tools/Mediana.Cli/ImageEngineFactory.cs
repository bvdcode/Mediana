// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Codecs.Jbig2;
using Mediana.Codecs.Jpeg;
using Mediana.Codecs.Jpeg2000;
using Mediana.Codecs.Png;
using Mediana.Core.Video;
using Mediana.Documents.Pdf;
using Mediana.Documents.Word;
using Mediana.Documents.OpenXml;
using Mediana.Images.Heif;
using Mediana.Images.Svg;
using Mediana.Images.Tgs;
using Mediana.Models.Mesh;
using Mediana.Processing;

namespace Mediana.Cli
{
    internal static class ImageEngineFactory
    {
        private const int MaximumDocumentPreviewDimension = 1200;

        public static MediaEngine Create() => AddDecoders(new MediaEngineBuilder()).Build();

        internal static MediaEngineBuilder AddDecoders(MediaEngineBuilder builder) => AddStillImageDecoders(builder)
            .AddImageDecoder(CreateJbig2(SampleAspectRatio.Square))
            .AddImageDecoder(CreateTgs())
            .AddImageDecoder(new MeshImageDecoder())
            .AddImageDecoder(new OpenXmlImageDecoder([new WordPageDecoder(new()
            {
                Package = CommandImageResources.PackageOptions,
                AllowFractionalMeasurements = true,
                DefaultPageSize = WordPageSize.A4
            }, renderOptions: new()
            {
                MaximumWidth = MaximumDocumentPreviewDimension, MaximumHeight = MaximumDocumentPreviewDimension,
                MaximumSamples = CommandImageResources.MaximumRgbaPixels, MaximumImagePixels = CommandImageResources.MaximumRgbaPixels,
                ImageColorResolver = SdrPreviewColorPolicy.Resolve
            }), new ThreeMfPageDecoder()], CommandImageResources.PackageOptions))
            .AddImageDecoder(new PdfImageDecoder(CommandImageResources.PdfReaderOptions, CommandImageResources.PdfRenderOptions with
            {
                MaximumWidth = MaximumDocumentPreviewDimension,
                MaximumHeight = MaximumDocumentPreviewDimension
            }));


        internal static MediaEngineBuilder AddStillImageDecoders(MediaEngineBuilder builder) => builder
            .UseResources(CommandTextResources.Shared)
            .AddImageDecoder(new PngImageDecoder(new()
            {
                MaximumEncodedBytes = CommandImageResources.MaximumBufferLength,
                MaximumPixels = CommandImageResources.MaximumRgbaPixels
            }))
            .AddImageDecoder(new JpegImageDecoder(new()
            {
                MaximumEncodedBytes = CommandImageResources.MaximumBufferLength,
                MaximumPixels = CommandImageResources.MaximumRgbaPixels
            }))
            .AddImageDecoder(CreateHeic(SampleAspectRatio.Square))
            .AddImageDecoder(CreateJpeg2000(SampleAspectRatio.Square))
            .AddImageDecoder(CreateSvg());

        public static TgsImageDecoder CreateTgs(int? width = null) => new(new()
        {
            IgnorePuppetEffects = true,
            IgnoreExpressions = true,
            MaximumInputBytes = CommandImageResources.MaximumBufferLength,
            MaximumJsonBytes = CommandImageResources.MaximumBufferLength
        }, CommandImageResources.DrawingOptions(width));

        public static IImageDecoder CreateSvg(int? width = null)
        {
            SvgReaderOptions reader = new()
            {
                MaximumInputBytes = CommandImageResources.MaximumBufferLength,
                MaximumImagePixels = CommandImageResources.MaximumRgbaPixels,
                ImageColorResolver = SdrPreviewColorPolicy.Resolve
            };
            return new SvgImageDecoder(reader, CommandImageResources.DrawingOptions(width));
        }

        public static HeicImageDecoder CreateHeic(SampleAspectRatio aspect) => new(aspect,
            new HeicDecodeOptions(maxInputBytes: CommandImageResources.MaximumBufferLength, maxPixels: CommandImageResources.MaximumRgbaPixels));
        public static Jpeg2000ImageDecoder CreateJpeg2000(SampleAspectRatio aspect) => new(aspect, new()
        {
            MaximumEncodedBytes = CommandImageResources.MaximumBufferLength,
            MaximumPixels = CommandImageResources.MaximumRgbaPixels,
            MaximumWorkingBytes = long.MaxValue
        });
        public static Jbig2ImageDecoder CreateJbig2(SampleAspectRatio aspect) => new(aspect, new()
        {
            MaximumEncodedBytes = CommandImageResources.MaximumBufferLength,
            MaximumPixels = CommandImageResources.MaximumRgbaPixels,
            MaximumDecodedPixels = long.MaxValue,
            MaximumWorkingBytes = long.MaxValue
        });
    }
}
