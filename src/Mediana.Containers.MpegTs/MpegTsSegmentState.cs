// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.MpegTs
{
    /// <summary>Immutable configuration, clock and packet-counter state before the next AVC video segment.
    /// Contains no I/O streams, packets or allocator-owned media buffers. Reusable by independent writers.</summary>
    public class MpegTsSegmentState
    {
        internal MpegTsSegmentState(MpegTsWriteOptions options, MpegTsTrackState[] tracks, MpegTsProgramState program)
        {
            Options = options;
            Tracks = Array.AsReadOnly(tracks);
            Program = program;
        }

        internal MpegTsWriteOptions Options { get; }
        internal IReadOnlyList<MpegTsTrackState> Tracks { get; }
        internal MpegTsProgramState Program { get; }
    }
}
