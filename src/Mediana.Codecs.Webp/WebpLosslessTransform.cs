// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal record WebpLosslessTransform(int Type, int Width, int Bits, Memory<byte> Data);
}
