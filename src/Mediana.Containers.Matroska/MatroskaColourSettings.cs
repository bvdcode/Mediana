// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    /// <summary>Native Colour values, including schema defaults and unmodified registered codes.</summary>
    public record MatroskaColourSettings
    {
        public ulong MatrixCoefficients { get; init; } = 2;
        public ulong BitsPerChannel { get; init; }
        public ulong? ChromaSubsamplingHorz { get; init; }
        public ulong? ChromaSubsamplingVert { get; init; }
        public ulong? CbSubsamplingHorz { get; init; }
        public ulong? CbSubsamplingVert { get; init; }
        public ulong ChromaSitingHorz { get; init; }
        public ulong ChromaSitingVert { get; init; }
        public ulong Range { get; init; }
        public ulong TransferCharacteristics { get; init; } = 2;
        public ulong Primaries { get; init; } = 2;
        public ulong? MaxCll { get; init; }
        public ulong? MaxFall { get; init; }
        public bool HasMasteringMetadata { get; init; }
    }
}
