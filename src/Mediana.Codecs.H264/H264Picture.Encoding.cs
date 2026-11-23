// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public void ResetForEncoding()
        {
            ObjectDisposedException.ThrowIf(pixelMemory.IsEmpty, this);
            if (writablePixels is null || referenceLists != 1)
            {
                throw new InvalidOperationException("Only unpublished I/P reconstruction storage can be reused for encoding.");
            }
            storage.Span.Clear();
            Modes.Fill(byte.MaxValue);
            ReferenceIndices(H264ReferenceList.L0).Fill(byte.MaxValue);
            FirstMacroblock = 0;
        }
    }
}
