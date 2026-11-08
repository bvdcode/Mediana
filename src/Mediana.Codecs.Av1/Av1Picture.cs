// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Av1
{
    internal class Av1Picture(Av1SequenceHeader sequence, MediaBuffer pixels) : IDisposable
    {
        internal Av1SequenceHeader Sequence { get; } = sequence;
        internal MediaBuffer Pixels { get; } = pixels;
        public void Dispose() => Pixels.Dispose();
    }
}
