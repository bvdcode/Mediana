// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264VideoFormat
    {
        public static VideoFormat Create(H264SequenceParameterSet sequence, VideoColorDescription? colorDescription = null)
        {
            H264VuiParameters vui = sequence.Vui ?? new H264VuiParameters();
            int topLocation = vui.ChromaLocationTop ?? 0;
            int bottomLocation = vui.ChromaLocationBottom ?? 0;
            if (topLocation != bottomLocation)
            {
                throw new NotSupportedException("Different top and bottom field chroma locations are not represented in a progressive video format.");
            }
            return new VideoFormat(sequence.Width, sequence.Height, VideoPixelFormat.Yuv420P8,
                ReadColor(vui, topLocation), ReadSampleAspectRatio(vui), colorDescription);
        }

        internal static SampleAspectRatio? ReadSampleAspectRatio(H264VuiParameters? vui) =>
            vui is { AspectRatioWidth: > 0, AspectRatioHeight: > 0 }
                ? new SampleAspectRatio(vui.AspectRatioWidth, vui.AspectRatioHeight) : null;

        internal static VideoColorInfo ReadColor(H264VuiParameters? parameters, int? location)
        {
            H264VuiParameters vui = parameters ?? new H264VuiParameters();
            ChromaLocation chromaLocation = location switch
            {
                null => ChromaLocation.Unspecified,
                int code => Location(code)
            };
            ColorRange range = vui.FullRange switch
            {
                true => ColorRange.Full,
                false => ColorRange.Limited
            };
            return new VideoColorInfo(CicpColorimetry.GetPrimaries(vui.ColorPrimaries), CicpColorimetry.GetTransfer(vui.TransferCharacteristics),
                CicpColorimetry.GetMatrix(vui.MatrixCoefficients), range, chromaLocation);
        }

        private static ChromaLocation Location(int code) => code switch
        {
            0 => ChromaLocation.Left,
            1 => ChromaLocation.Center,
            2 => ChromaLocation.TopLeft,
            3 => ChromaLocation.Top,
            4 => ChromaLocation.BottomLeft,
            5 => ChromaLocation.Bottom,
            _ => throw new InvalidDataException("Invalid H.264 chroma location code.")
        };
    }
}
