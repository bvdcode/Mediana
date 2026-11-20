// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Explicit interpretation of JPEG component samples, before RGB presentation.</summary>
    public enum JpegColorSpace
    {
        Rgb,
        YCbCr,
        /// <summary>Four ink-coverage components, where zero means no ink.</summary>
        Cmyk,
        /// <summary>Adobe CMYK components, where 255 means no ink.</summary>
        InvertedCmyk,
        /// <summary>Adobe YCCK, including its inverted black component.</summary>
        Ycck
    }
}
