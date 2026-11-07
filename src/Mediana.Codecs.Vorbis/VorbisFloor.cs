// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Audio;

namespace Mediana.Codecs.Vorbis
{
    internal abstract class VorbisFloor : IDisposable
    {
        public abstract bool Decode(ref VorbisBitReader reader, IReadOnlyList<VorbisCodebook> books,
            Span<float> curve, Span<float> vector, CancellationToken token);
        public abstract void Dispose();
    }
}
