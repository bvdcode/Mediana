// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Formats.Video
{
    /// <summary>An unfinished NAL position captured by <see cref="H264BitWriter.CreateCheckpoint"/>.</summary>
    public readonly struct H264BitWriterCheckpoint
    {
        internal H264BitWriterCheckpoint(H264BitWriter writer, int revision, int length, int zeroBytes,
            int partial, int partialBits, int preventionBytes, long bitPosition)
        {
            Writer = writer;
            Revision = revision;
            Length = length;
            ZeroBytes = zeroBytes;
            Partial = partial;
            PartialBits = partialBits;
            PreventionBytes = preventionBytes;
            BitPosition = bitPosition;
        }

        internal H264BitWriter? Writer { get; }
        internal int Revision { get; }
        internal int Length { get; }
        internal int ZeroBytes { get; }
        internal int Partial { get; }
        internal int PartialBits { get; }
        internal int PreventionBytes { get; }

        /// <summary>Gets the RBSP bit position before subsequent writes.</summary>
        public long BitPosition { get; }
    }
}
