// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Core.Video
{
    public enum MatrixCoefficients
    {
        Unspecified,
        Rgb,
        Bt601,
        Bt709,
        Bt2020NonConstant,
        Fcc,
        Smpte240M,
        YCgCo,
        Bt2020Constant,
        Smpte2085,
        ChromaticityNonConstant,
        ChromaticityConstant,
        ICtCp,
        IptC2,
        YCgCoRe,
        YCgCoRo
    }
}
