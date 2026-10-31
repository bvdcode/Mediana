// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal class H264EncodedPicture(H264Picture reconstruction, MediaBuffer data, int quantizer) : IDisposable
    {
        private H264Picture? picture = reconstruction;
        public MediaBuffer Data { get; } = data;
        public int Quantizer { get; } = quantizer;

        public H264Picture TakeReference()
        {
            H264Picture result = picture ?? throw new InvalidOperationException("The reconstructed reference was already transferred.");
            picture = null;
            return result;
        }

        public void Dispose()
        {
            Data.Dispose();
            picture?.Dispose();
            picture = null;
        }
    }
}
