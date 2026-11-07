// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Audio
{
    internal readonly record struct VorbisFloor0Definition(int Order, int Rate, int BarkMapSize, int AmplitudeBits, int AmplitudeOffset);
}
