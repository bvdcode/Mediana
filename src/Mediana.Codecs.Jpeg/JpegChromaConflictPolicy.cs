// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    /// <summary>Resolves contradictory JFIF and Exif chroma sample positions.</summary>
    public enum JpegChromaConflictPolicy
    {
        Reject,
        /// <summary>Use centered JFIF positioning and report a decoding warning.</summary>
        UseJfif
    }
}
