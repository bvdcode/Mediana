// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana.IO
{
    public record PcmStreamWriteResult(long SamplesPerChannel, AudioFormat? Format, long BytesWritten);
}
