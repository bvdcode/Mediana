// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    /// <summary>Prepares mono/stereo interleaved Float32 for an explicitly registered audio encoder.</summary>
    public record AudioTranscodeOptions
    {
        public int? StreamIndex { get; init; }
        /// <summary>Explicit mono/stereo downmix; null preserves the source channel layout.</summary>
        public int? Channels { get; init; }
        public MediaOpenOptions Input { get; init; } = new();
    }
}
