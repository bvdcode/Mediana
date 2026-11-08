// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1SequenceHeader(int Width, int Height, bool Superblock128,
        bool FilterIntra, bool IntraEdgeFilter, bool SuperResolution, bool FilmGrain, bool FullRange,
        int ColorPrimaries, int TransferCharacteristics, int MatrixCoefficients)
    {
        internal int MiColumns => 2 * ((Width + 7) >> 3);
        internal int MiRows => 2 * ((Height + 7) >> 3);
        internal int Stride => MiColumns * 4;
        internal int PaddedHeight => MiRows * 4;

        internal static Av1SequenceHeader Parse(ReadOnlySpan<byte> encoded)
        {
            Av1BitReader bits = new(encoded);
            int profile = bits.Read(3);
            bool stillPicture = bits.Flag();
            bool reduced = bits.Flag();
            if (profile != 0 || !stillPicture || !reduced)
            {
                throw new NotSupportedException("AV1 reconstruction currently requires Main-profile reduced still-picture headers.");
            }
            bits.Read(5);
            int widthBits = bits.Read(4) + 1;
            int heightBits = bits.Read(4) + 1;
            int width = bits.Read(widthBits) + 1;
            int height = bits.Read(heightBits) + 1;
            bool largeSuperblock = bits.Flag();
            bool filterIntra = bits.Flag();
            bool edgeFilter = bits.Flag();
            bool superResolution = bits.Flag();
            bits.Flag();
            bits.Flag();
            if (bits.Flag())
            {
                throw new NotSupportedException("AV1 high-precision reconstruction is not implemented.");
            }
            if (!bits.Flag())
            {
                throw new NotSupportedException("AV1 chroma reconstruction is not implemented.");
            }
            int primaries = 2;
            int transfer = 2;
            int matrix = 2;
            if (bits.Flag())
            {
                primaries = bits.Read(8);
                transfer = bits.Read(8);
                matrix = bits.Read(8);
            }
            bool fullRange = bits.Flag();
            bool filmGrain = bits.Flag();
            bits.Finish();
            return new(width, height, largeSuperblock, filterIntra, edgeFilter, superResolution,
                filmGrain, fullRange, primaries, transfer, matrix);
        }
    }
}
