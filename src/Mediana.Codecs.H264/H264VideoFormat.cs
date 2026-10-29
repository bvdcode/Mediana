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
            VideoColorInfo color = new(Primaries(vui.ColorPrimaries), Transfer(vui.TransferCharacteristics),
                Matrix(vui.MatrixCoefficients), vui.FullRange ? ColorRange.Full : ColorRange.Limited, Location(topLocation));
            return new VideoFormat(sequence.Width, sequence.Height, VideoPixelFormat.Yuv420P8, color, aspect);
        }

        // Rec. ITU-T H.264, Tables E-3 through E-5. Reserved CICP codes are interpreted as unspecified.
        private static ColorPrimaries Primaries(int code) => code switch
        {
            1 => ColorPrimaries.Bt709,
            4 => ColorPrimaries.Bt470M,
            5 => ColorPrimaries.Bt470Bg,
            6 or 7 => ColorPrimaries.Smpte170M,
            8 => ColorPrimaries.Film,
            9 => ColorPrimaries.Bt2020,
            10 => ColorPrimaries.Xyz,
            11 => ColorPrimaries.DciP3,
            12 => ColorPrimaries.DisplayP3,
            22 => ColorPrimaries.Ebu3213,
            0 or 2 or 3 or >= 13 and <= 21 or >= 23 and <= 255 => ColorPrimaries.Unspecified,
            _ => throw new InvalidDataException("Invalid H.264 color primaries code.")
        };

        private static TransferFunction Transfer(int code) => code switch
        {
            1 or 6 or 14 or 15 => TransferFunction.Bt709,
            4 => TransferFunction.Gamma22,
            5 => TransferFunction.Gamma28,
            7 => TransferFunction.Smpte240M,
            8 => TransferFunction.Linear,
            9 => TransferFunction.Log100,
            10 => TransferFunction.Log316,
            11 => TransferFunction.Iec61966_2_4,
            12 => TransferFunction.Bt1361Extended,
            13 => TransferFunction.Srgb,
            16 => TransferFunction.Pq,
            17 => TransferFunction.Smpte428,
            18 => TransferFunction.Hlg,
            0 or 2 or 3 or >= 19 and <= 255 => TransferFunction.Unspecified,
            _ => throw new InvalidDataException("Invalid H.264 transfer characteristics code.")
        };

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
