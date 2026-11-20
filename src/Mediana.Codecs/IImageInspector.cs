// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs
{
    /// <summary>Recognizes and inspects images independently of pixel decoding. Recognition does not validate the complete input.</summary>
    public interface IImageInspector : IImageInfoReader
    {
        string ImplementationId { get; }
        int ProbeLength { get; }
        bool CanRead(ReadOnlySpan<byte> prefix, long? encodedLength = null);
    }
}
