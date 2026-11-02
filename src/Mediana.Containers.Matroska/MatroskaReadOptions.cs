// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Matroska
{
    public record MatroskaReadOptions
    {
        public int MaxHeaderBytes { get; init; } = 64 * 1024;
        /// <summary>Aggregate Info/Tracks payloads and attachment declarations, excluding FileData bytes.</summary>
        public int MaxMetadataBytes { get; init; } = 32 * 1024 * 1024;
        public int MaxIndexBytes { get; init; } = 64 * 1024 * 1024;
        public int MaxCodecConfigurationBytes { get; init; } = 64 * 1024;
        public int MaxTotalCodecConfigurationBytes { get; init; } = 4 * 1024 * 1024;
        public int MaxTracks { get; init; } = 128;
        public int MaxAttachments { get; init; } = 128;
        /// <summary>Maximum bytes returned by one attachment read. Larger files can still be indexed.</summary>
        public int MaxAttachmentBytes { get; init; } = 32 * 1024 * 1024;
        public int MaxTextBytes { get; init; } = 1024 * 1024;
        /// <summary>Maximum elements visited by each metadata or indexing pass.</summary>
        public int MaxElements { get; init; } = 10_000_000;
        public int MaxBlocks { get; init; } = 10_000_000;
        public long MaxPacketsPerTrack { get; init; } = 10_000_000;
        public int MaxBlockHeaderBytes { get; init; } = 64 * 1024;
        public int MaxFrameBytes { get; init; } = 32 * 1024 * 1024;
        /// <summary>Also verify Segment, Cluster and attachment CRCs, which reads their complete payloads.
        /// EBML header and parsed Info/Tracks metadata CRCs are always checked.</summary>
        public bool VerifyMediaChecksums { get; init; }

        internal MatroskaIndexOptions IndexOptions => new(MaxIndexBytes, MaxBlocks, MaxPacketsPerTrack,
            MaxBlockHeaderBytes, MaxFrameBytes, VerifyMediaChecksums);
        internal MatroskaMetadataLimits MetadataLimits => new(MaxTracks, MaxTextBytes, MaxElements);

        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxHeaderBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxMetadataBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxCodecConfigurationBytes);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTotalCodecConfigurationBytes);
            ArgumentOutOfRangeException.ThrowIfNegative(MaxAttachments);
            ArgumentOutOfRangeException.ThrowIfNegative(MaxAttachmentBytes);
            IndexOptions.Validate();
            MetadataLimits.Validate();
        }
    }
}
