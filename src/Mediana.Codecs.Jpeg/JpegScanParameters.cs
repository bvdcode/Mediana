// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal readonly record struct JpegScanParameters(int First, int Last, int High, int Low)
    {
        internal void Validate(bool progressive, int count)
        {
            if (!progressive)
            {
                if (First != 0 || Last != 63 || High != 0 || Low != 0)
                {
                    throw new InvalidDataException("Sequential JPEG scans require the complete coefficient band without approximation.");
                }
                return;
            }
            if (First > Last || Last > 63 || (First == 0 && Last != 0) || (First != 0 && count != 1) ||
                High > 13 || Low > 13 || (High != 0 && High != Low + 1))
            {
                throw new InvalidDataException("Progressive JPEG spectral band, component count or approximation is invalid.");
            }
        }
    }
}
