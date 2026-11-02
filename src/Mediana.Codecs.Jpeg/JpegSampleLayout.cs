// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal readonly record struct JpegSampleLayout(int Width, int Height, int MaximumHorizontal,
        int MaximumVertical, IReadOnlyList<JpegComponent> Components);
}
