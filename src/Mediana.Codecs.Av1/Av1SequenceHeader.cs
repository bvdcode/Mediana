// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1SequenceHeader(int Width, int Height, bool Monochrome, bool SeparateUvDeltaQ, int ChromaPosition, bool Superblock128,
        bool FilterIntra, bool IntraEdgeFilter, bool SuperResolution, bool FilmGrain, bool FullRange,
        int ColorPrimaries, int TransferCharacteristics, int MatrixCoefficients)
    {
        internal int MiColumns => 2 * ((Width + 7) >> 3);
        internal int MiRows => 2 * ((Height + 7) >> 3);
        internal int Stride => MiColumns * 4;
        internal int PaddedHeight => MiRows * 4;
        internal int LumaSamples => checked(Stride * PaddedHeight);
        internal int SampleCount => checked(LumaSamples + (Monochrome ? 0 : LumaSamples / 2));
        internal int ContextCount => checked((MiColumns + Av1TileDecoder.LeftContexts) * (Monochrome ? 1 : 2));

        internal int PlaneShift(int plane) => plane switch
        {
            0 => 0,
            1 or 2 when !Monochrome => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };

        internal int PlaneStride(int plane) => Stride >> PlaneShift(plane);
        internal int PlaneHeight(int plane) => PaddedHeight >> PlaneShift(plane);
        internal int PlaneColumns(int plane) => MiColumns >> PlaneShift(plane);
        internal int PlaneRows(int plane) => MiRows >> PlaneShift(plane);
        internal int PlaneOffset(int plane) => plane switch
        {
            0 => 0,
            1 when !Monochrome => LumaSamples,
            2 when !Monochrome => checked(LumaSamples + LumaSamples / 4),
            _ => throw new ArgumentOutOfRangeException(nameof(plane))
        };

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
            bool monochrome = bits.Flag();
            int primaries = 2;
            int transfer = 2;
            int matrix = 2;
            if (bits.Flag())
            {
                primaries = bits.Read(8);
                transfer = bits.Read(8);
                matrix = bits.Read(8);
            }
            if (!monochrome && primaries == 1 && transfer == 13 && matrix == 0)
            {
                throw new NotSupportedException("AV1 RGB reconstruction is not implemented.");
            }
            bool fullRange = bits.Flag();
            int chromaPosition = 0;
            bool separateUv = false;
            if (!monochrome)
            {
                chromaPosition = bits.Read(2);
                if (chromaPosition == 3)
                {
                    throw new InvalidDataException("The AV1 chroma sample position is reserved.");
                }
                separateUv = bits.Flag();
            }
            bool filmGrain = bits.Flag();
            bits.Finish();
            return new(width, height, monochrome, separateUv, chromaPosition, largeSuperblock, filterIntra, edgeFilter, superResolution,
                filmGrain, fullRange, primaries, transfer, matrix);
        }
    }
}
