// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264VideoFormat
    {
        public static VideoFormat Create(H264SequenceParameterSet sequence, VideoColorDescription? colorDescription = null,
            VideoAspectRatioDescription? aspectRatioDescription = null, VideoScanMode scanMode = VideoScanMode.Progressive)
        {
            H264VuiParameters vui = sequence.Vui ?? new H264VuiParameters();
            (int topLocation, int bottomLocation) = sequence.ChromaFormat switch
            {
                1 => (vui.ChromaLocationTop ?? 0, vui.ChromaLocationBottom ?? 0),
                2 or 3 => (2, 2),
                _ => throw new NotSupportedException("The H.264 decoded chroma layout is unsupported.")
            };
            if (scanMode == VideoScanMode.Progressive && topLocation != bottomLocation)
            {
                throw new NotSupportedException("Different top and bottom field chroma locations are not represented in a progressive video format.");
            }
            SampleAspectRatio? codecAspect = ReadSampleAspectRatio(vui);
            SampleAspectRatio? aspect = aspectRatioDescription?.Resolve(codecAspect) ?? codecAspect;
            VideoPixelFormat pixels = ReadPixelFormat(sequence)
                ?? throw new NotSupportedException("The H.264 decoded chroma layout is unsupported.");
            int height = scanMode switch
            {
                VideoScanMode.Progressive or VideoScanMode.TopFieldFirst or VideoScanMode.BottomFieldFirst => sequence.Height,
                VideoScanMode.TopField or VideoScanMode.BottomField => sequence.Height / 2,
                _ => throw new ArgumentOutOfRangeException(nameof(scanMode))
            };
            bool rgb = pixels is PlanarVideoPixelFormat { ColorModel: PlanarColorModel.Gbr };
            VideoColorInfo color = ReadColor(vui, !rgb && topLocation == bottomLocation ? topLocation : null);
            if (scanMode != VideoScanMode.Progressive && !rgb)
            {
                color = color with { FieldChromaLocations = new(H264VuiParameters.GetChromaLocation(topLocation), H264VuiParameters.GetChromaLocation(bottomLocation)) };
            }
            return new VideoFormat(sequence.Width, height, pixels, color, aspect, colorDescription, scanMode, sequence.Height);
        }

        internal static VideoPixelFormat? ReadPixelFormat(H264SequenceParameterSet sequence) =>
            (sequence.ChromaFormat, sequence.LumaBitDepth) switch
            {
                (1, 8) => VideoPixelFormat.Yuv420P8,
                (2, 8) => VideoPixelFormat.Yuv422P8,
                (1, 10) => VideoPixelFormat.Yuv420P10,
                (2, 10) => VideoPixelFormat.Yuv422P10,
                (3, 8) when sequence.Vui?.MatrixCoefficients == 0 => VideoPixelFormat.GbrP8,
                (3, 10) when sequence.Vui?.MatrixCoefficients == 0 => VideoPixelFormat.GbrP10,
                (3, 8) => VideoPixelFormat.Yuv444P8,
                (3, 10) => VideoPixelFormat.Yuv444P10,
                _ => null
            };

        internal static SampleAspectRatio? ReadSampleAspectRatio(H264VuiParameters? vui) =>
            vui is { AspectRatioWidth: > 0, AspectRatioHeight: > 0 }
                ? new SampleAspectRatio(vui.AspectRatioWidth, vui.AspectRatioHeight) : null;

        internal static VideoColorInfo ReadColor(H264VuiParameters? parameters, int? location)
        {
            H264VuiParameters vui = parameters ?? new H264VuiParameters();
            ChromaLocation chromaLocation = location switch
            {
                null => ChromaLocation.Unspecified,
                int code => H264VuiParameters.GetChromaLocation(code)
            };
            ColorRange range = vui.FullRange switch
            {
                true => ColorRange.Full,
                false => ColorRange.Limited
            };
            return new VideoColorInfo(CicpColorimetry.GetPrimaries(vui.ColorPrimaries), CicpColorimetry.GetTransfer(vui.TransferCharacteristics),
                CicpColorimetry.GetMatrix(vui.MatrixCoefficients), range, chromaLocation);
        }
    }
}
