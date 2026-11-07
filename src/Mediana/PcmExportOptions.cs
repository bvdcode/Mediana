// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana
{
    public record PcmExportOptions
    {
        public int? StreamIndex { get; init; }
        public AudioSampleFormat SampleFormat { get; init; } = AudioSampleFormat.Signed16;
        public MediaOpenOptions Input { get; init; } = new();
    }
}
