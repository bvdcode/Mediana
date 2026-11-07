// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    public record VideoTranscodeOptions
    {
        public int? StreamIndex { get; init; }
        /// <summary>Explicitly omit audio tracks. Without this choice, an audio-bearing input is rejected.</summary>
        public bool OmitAudio { get; init; }
        public int? MaximumWidth { get; init; }
        public int? MaximumHeight { get; init; }
        public MediaOpenOptions Input { get; init; } = new();
    }
}
