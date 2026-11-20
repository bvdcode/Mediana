// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Mp3
{
    public record Mp3EncoderOptions
    {
        public int BitRate { get; init; } = 128000;
        public int MaximumInputSamplesPerChannel { get; init; } = 65536;
    }
}
