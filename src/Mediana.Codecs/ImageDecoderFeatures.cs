// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    [Flags]
    public enum ImageDecoderFeatures
    {
        SingleImage = 0,
        Pages = 1,
        Animation = 2
    }
}
