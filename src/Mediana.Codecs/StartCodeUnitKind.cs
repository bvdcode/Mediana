// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    internal enum StartCodeUnitKind
    {
        Other,
        Picture,
        PictureContinuation,
        NextPicturePrefix,
        Incomplete
    }
}
