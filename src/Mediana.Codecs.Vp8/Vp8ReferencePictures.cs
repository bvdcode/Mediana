// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Vp8
{
    internal class Vp8ReferencePictures : IDisposable
    {
        private readonly MediaBuffer?[] pictures = new MediaBuffer?[4];
        internal ReadOnlyMemory<byte>[] GetMemory()
        {
            ReadOnlyMemory<byte>[] result = new ReadOnlyMemory<byte>[4];
            for (int i = 1; i < 4; i++) { result[i] = pictures[i]?.Memory ?? ReadOnlyMemory<byte>.Empty; }
            return result;
        }

        internal void Update(Vp8FrameHeader header, MediaBuffer current)
        {
            if (header.CopyAlternate != 0)
            { Replace(3, pictures[header.CopyAlternate switch { 1 => 1, 2 => 2, _ => throw new InvalidDataException("Invalid VP8 alternate reference copy.") }]); }
            if (header.CopyGolden != 0)
            { Replace(2, pictures[header.CopyGolden switch { 1 => 1, 2 => 3, _ => throw new InvalidDataException("Invalid VP8 golden reference copy.") }]); }
            if (header.RefreshGolden) { Replace(2, current); }
            if (header.RefreshAlternate) { Replace(3, current); }
            if (header.RefreshLast) { Replace(1, current); }
        }

        private void Replace(int index, MediaBuffer? picture)
        {
            MediaBuffer retained = (picture ?? throw new InvalidDataException("VP8 reference update precedes its keyframe.")).Retain();
            pictures[index]?.Dispose(); pictures[index] = retained;
        }

        public void Dispose()
        {
            for (int i = 1; i < 4; i++) { pictures[i]?.Dispose(); pictures[i] = null; }
        }
    }
}
