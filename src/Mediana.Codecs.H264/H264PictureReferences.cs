// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal record H264PictureReferences(int FirstMacroblock, H264PictureIdentity?[] L0, H264PictureIdentity?[] L1);
}
