// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.ProRes
{
    public class ProResDecoderOptions
    {
        /// <summary>Accept older alpha encoders that pad the last slice to sixteen rows or omit its final single-sample run.</summary>
        public bool RecoverLegacyAlpha { get; init; }
    }
}
