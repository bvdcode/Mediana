// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Codecs
{
    /// <summary>Optional decoder capability for validating packet-index seek candidates.</summary>
    public interface IRandomAccessDecoderFactory
    {
        IRandomAccessInspector CreateRandomAccessInspector(EncodedStreamInfo stream);
    }
}
