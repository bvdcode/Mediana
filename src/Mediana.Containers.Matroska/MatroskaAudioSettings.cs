// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaAudioSettings(double SamplingFrequency, double OutputSamplingFrequency,
        ulong Channels, ulong? BitDepth);
}
