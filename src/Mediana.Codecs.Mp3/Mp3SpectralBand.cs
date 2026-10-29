// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    internal readonly record struct Mp3SpectralBand(int Offset, int Width, int Band, int Window, int FrequencyStart);
}
