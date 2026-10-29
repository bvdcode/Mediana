// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Video;

namespace Mediana.Codecs.H264
{
    internal static class H264VideoFormat
    {
        public static VideoFormat Create(H264SequenceParameterSet sequence)
        {
            H264VuiParameters vui = sequence.Vui ?? new H264VuiParameters();
            int topLocation = vui.ChromaLocationTop ?? 0;
            int bottomLocation = vui.ChromaLocationBottom ?? 0;
            if (topLocation != bottomLocation)
            {
                throw new NotSupportedException("Different top and bottom field chroma locations are not represented in a progressive video format.");
            }
            SampleAspectRatio? aspect = vui.AspectRatioWidth > 0 && vui.AspectRatioHeight > 0
                ? new SampleAspectRatio(vui.AspectRatioWidth, vui.AspectRatioHeight) : null;
            VideoColorInfo color = new(CicpColorimetry.GetPrimaries(vui.ColorPrimaries), CicpColorimetry.GetTransfer(vui.TransferCharacteristics),
                Matrix(vui.MatrixCoefficients), vui.FullRange ? ColorRange.Full : ColorRange.Limited, Location(topLocation));
            return new VideoFormat(sequence.Width, sequence.Height, VideoPixelFormat.Yuv420P8, color, aspect);
        }

        private static MatrixCoefficients Matrix(int code) => code switch
        {
            0 => MatrixCoefficients.Rgb,
            1 => MatrixCoefficients.Bt709,
            2 or 3 or >= 18 and <= 255 => MatrixCoefficients.Unspecified,
            4 => MatrixCoefficients.Fcc,
            5 or 6 => MatrixCoefficients.Bt601,
            7 => MatrixCoefficients.Smpte240M,
            8 => MatrixCoefficients.YCgCo,
            9 => MatrixCoefficients.Bt2020NonConstant,
            10 => MatrixCoefficients.Bt2020Constant,
            11 => MatrixCoefficients.Smpte2085,
            12 => MatrixCoefficients.ChromaticityNonConstant,
            13 => MatrixCoefficients.ChromaticityConstant,
            14 => MatrixCoefficients.ICtCp,
            15 => MatrixCoefficients.IptC2,
            16 => MatrixCoefficients.YCgCoRe,
            17 => MatrixCoefficients.YCgCoRo,
            _ => throw new InvalidDataException("Invalid H.264 matrix coefficients code.")
        };

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
