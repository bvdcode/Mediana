// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal readonly record struct Av1SequenceHeader(int Width, int Height, bool Monochrome, bool SeparateUvDeltaQ, int ChromaPosition, bool Superblock128,
        bool FilterIntra, bool IntraEdgeFilter, bool SuperResolution, bool Cdef, bool Restoration, bool FilmGrain, bool FullRange,
        int ColorPrimaries, int TransferCharacteristics, int MatrixCoefficients, Av1SequenceTools Tools, Av1SequenceTiming? Timing = null, int BitDepth = 8)
    {
        internal int MiColumns => 2 * ((Width + 7) >> 3);
        internal int MiRows => 2 * ((Height + 7) >> 3);
        internal int Stride => MiColumns * 4;
        internal int PaddedHeight => MiRows * 4;
        internal int LumaSamples => checked(Stride * PaddedHeight);
        internal int SampleCount => checked(LumaSamples + (Monochrome ? 0 : LumaSamples / 2));
        internal int SampleByteCount => BitDepth switch
        {
            8 => SampleCount,
            10 or 12 => checked(SampleCount * 2),
            _ => throw new InvalidDataException("The AV1 sample depth is invalid.")
        };
        internal int ContextCount => checked((MiColumns + Av1TileDecoder<byte>.LeftContexts) * (Monochrome ? 1 : 2));

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
            if (profile != 0)
            {
                throw new NotSupportedException("AV1 reconstruction currently requires Main profile.");
            }
            if (reduced && !stillPicture)
            {
                throw new InvalidDataException("An AV1 reduced header requires still_picture.");
            }
            int level;
            bool tier;
            Av1SequenceTiming? timing = null;
            if (reduced)
            {
                level = bits.Read(5);
                tier = false;
            }
            else
            {
                timing = Av1SequenceTiming.Read(ref bits, out level, out tier);
            }
            int widthBits = bits.Read(4) + 1;
            int heightBits = bits.Read(4) + 1;
            int width = bits.Read(widthBits) + 1;
            int height = bits.Read(heightBits) + 1;
            int deltaIdBits = 0;
            int frameIdBits = 0;
            if (!reduced && bits.Flag())
            {
                deltaIdBits = bits.Read(4) + 2;
                frameIdBits = deltaIdBits + bits.Read(3) + 1;
                if (frameIdBits > 16)
                {
                    throw new InvalidDataException("The AV1 frame identifier length exceeds sixteen bits.");
                }
            }
            bool largeSuperblock = bits.Flag();
            bool filterIntra = bits.Flag();
            bool edgeFilter = bits.Flag();
            Av1SequenceTools tools = ReadTools(ref bits, reduced, widthBits, heightBits, level, tier, frameIdBits, deltaIdBits);
            bool superResolution = bits.Flag();
            bool cdef = bits.Flag();
            bool restoration = bits.Flag();
            int bitDepth = bits.Flag() ? 10 : 8;
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
            return new(width, height, monochrome, separateUv, chromaPosition, largeSuperblock, filterIntra, edgeFilter, superResolution, cdef, restoration,
                filmGrain, fullRange, primaries, transfer, matrix, tools, timing, bitDepth);
        }

        private static Av1SequenceTools ReadTools(ref Av1BitReader bits, bool reduced, int widthBits, int heightBits, int level, bool tier, int frameIdBits, int deltaIdBits)
        {
            int screenContent = 2;
            int integerMotion = 2;
            int orderBits = 0;
            bool interIntra = false;
            bool maskedCompound = false;
            bool warpedMotion = false;
            bool dualFilter = false;
            bool jointCompound = false;
            bool referenceMotion = false;
            if (!reduced)
            {
                interIntra = bits.Flag();
                maskedCompound = bits.Flag();
                warpedMotion = bits.Flag();
                dualFilter = bits.Flag();
                bool orderHint = bits.Flag();
                if (orderHint)
                {
                    jointCompound = bits.Flag();
                    referenceMotion = bits.Flag();
                }
                if (!bits.Flag())
                {
                    screenContent = bits.Flag() ? 1 : 0;
                }
                if (screenContent > 0 && !bits.Flag())
                {
                    integerMotion = bits.Flag() ? 1 : 0;
                }
                if (orderHint)
                {
                    orderBits = bits.Read(3) + 1;
                }
            }
            return new(reduced, widthBits, heightBits, orderBits, screenContent, integerMotion, level, tier, frameIdBits, deltaIdBits, interIntra, maskedCompound, warpedMotion, dualFilter, jointCompound, referenceMotion);
        }
    }
}
