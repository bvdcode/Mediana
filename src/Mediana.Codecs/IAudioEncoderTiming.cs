// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    /// <summary>Codec delay and final padding, in samples per channel. Padding becomes known after draining the encoder.</summary>
    public interface IAudioEncoderTiming
    {
        int PrimingSamples { get; }
        int? PaddingSamples { get; }
    }
}
