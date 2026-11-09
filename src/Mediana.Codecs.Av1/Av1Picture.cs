// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1
{
    internal class Av1Picture(Av1FrameHeader header, MediaBuffer pixels) : IDisposable
    {
        internal Av1FrameHeader Header { get; } = header;
        internal Av1SequenceHeader Sequence => Header.Geometry;
        internal MediaBuffer Pixels { get; } = pixels;
        public void Dispose() => Pixels.Dispose();
    }
}
