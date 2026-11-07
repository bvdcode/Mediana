// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal class Vp9ReferencePictures : IDisposable
    {
        private readonly Vp9Picture?[] pictures = new Vp9Picture?[8];
        internal Vp9Picture Get(int index) => pictures[index] ?? throw new InvalidDataException("A VP9 reference picture has not been initialized.");
        internal void Update(Vp9FrameHeader header, Vp9Picture current)
        {
            for (int i = 0; i < pictures.Length; i++)
            {
                if ((header.RefreshReferences & (1 << i)) == 0) { continue; }
                Vp9Picture retained = current.Retain(); pictures[i]?.Dispose(); pictures[i] = retained;
            }
        }
        public void Dispose()
        {
            for (int i = 0; i < pictures.Length; i++) { pictures[i]?.Dispose(); pictures[i] = null; }
        }
    }
}
