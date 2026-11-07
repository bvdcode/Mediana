// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana
{
    public record PcmExportResult(long SamplesPerChannel, AudioFormat Format, long BytesWritten);
}
