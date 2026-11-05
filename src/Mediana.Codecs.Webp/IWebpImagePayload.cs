// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Webp
{
    internal interface IWebpImagePayload : IDisposable
    {
        int PayloadLength { get; }
        bool HasAlpha { get; }
        ReadOnlySpan<byte> FourCc { get; }
        void Write(Memory<byte> destination, CancellationToken cancellationToken);
    }
}
