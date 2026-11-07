// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Tiff;

namespace Mediana.Codecs.Tiff
{
    internal class TiffImageData
    {
        internal readonly TiffDirectoryReader Reader;
        internal readonly List<TiffDirectory> Pages = [];
        internal TiffImageData(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            Reader = new(encoded); HashSet<ulong> offsets = [];
            for (ulong offset = Reader.FirstOffset; offset != 0;)
            {
                token.ThrowIfCancellationRequested();
                if (!offsets.Add(offset)) { throw new InvalidDataException("Cyclic TIFF page directory."); }
                TiffDirectory directory = Reader.Read(offset, token); Pages.Add(directory); offset = directory.NextOffset;
            }
            if (Pages.Count == 0) { throw new InvalidDataException("TIFF has no image directory."); }
        }
    }
}
