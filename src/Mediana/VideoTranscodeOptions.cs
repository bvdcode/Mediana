// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Processing;

namespace Mediana
{
    public record VideoTranscodeOptions : VideoPreparationOptions
    {
        public int? StreamIndex { get; init; }
        /// <summary>Explicitly omit audio tracks. Without this choice, an audio-bearing input is rejected.</summary>
        public bool OmitAudio { get; init; }
        public MediaOpenOptions Input { get; init; } = new();
    }
}
