// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Execution;

namespace Mediana
{
    public record AudioTranscodeResult(long SamplesPerChannel, MediaOperationProgress Encoding,
        int? PrimingSamples, int? PaddingSamples);
}
