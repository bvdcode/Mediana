// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Streaming.Hls;

namespace Mediana.Sample
{
    internal record HlsPreparedAudio(HlsAudioRendition Rendition, long SamplesPerChannel, int SampleRate,
        int PrimingSamples, int PaddingSamples);
}
