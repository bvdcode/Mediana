// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    /// <summary>Maps CICP codes to color metadata. Reserved codes remain unspecified.</summary>
    public static class CicpColorimetry
    {
        // Rec. ITU-T H.273 (07/2024), Tables 2–4.
        public static ColorPrimaries GetPrimaries(int code) => code switch
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
            _ => throw new InvalidDataException("Invalid CICP color primaries code.")
        };

        public static MatrixCoefficients GetMatrix(int code) => code switch
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
            _ => throw new InvalidDataException("Invalid CICP matrix coefficients code.")
        };
        public static TransferFunction GetTransfer(int code) => code switch
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
            _ => throw new InvalidDataException("Invalid CICP transfer characteristics code.")
        };

    }
}
