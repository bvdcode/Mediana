// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    internal readonly record struct MatroskaAudioFrameTiming(int SampleRate, int Samples, int PrecedingSamples, int FrameBytes);
}
