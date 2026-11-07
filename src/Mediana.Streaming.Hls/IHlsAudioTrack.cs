// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Mp4;

namespace Mediana.Streaming.Hls
{
    internal interface IHlsAudioTrack
    {
        Mp4AudioTrackDefinition Definition { get; }
        MediaTrackRequest? CreateRequest(HlsVideoWindow window);
    }
}
