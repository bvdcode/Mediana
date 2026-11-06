// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal record WebpDecodedFrame(int Left, int Top, int Width, int Height, int Duration,
        bool Replace, bool Dispose, bool Lossless, ReadOnlyMemory<byte> Image, ReadOnlyMemory<byte> Alpha);
}
