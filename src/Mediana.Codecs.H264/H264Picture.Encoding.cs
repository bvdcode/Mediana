// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal partial class H264Picture
    {
        public void ResetForEncoding()
        {
            ObjectDisposedException.ThrowIf(lumaPixels.IsEmpty, this);
            if (writablePixels is null || referenceLists != 1)
            {
                throw new InvalidOperationException("Only unpublished I/P reconstruction storage can be reused for encoding.");
            }
            Metadata.Clear();
            Modes.Fill(byte.MaxValue);
            ReferenceIndices(H264ReferenceList.L0).Fill(byte.MaxValue);
            FirstMacroblock = 0;
        }
    }
}
