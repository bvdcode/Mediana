// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.MpegTs
{
    public partial class MpegTsWriter
    {
        /// <summary>Flushes the previous destination and continues the same program in a borrowed writable stream.
        /// Requires a preceding packet and either one audio track or exactly one AVC track with optional audio.
        /// The next packet must belong to the clock track; AVC requires an IDR access unit.
        /// Preserves timestamps, continuity counters and clock state; emits PAT/PMT before the new segment's payload.</summary>
        public async ValueTask BeginSegmentAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            Enter();
            bool success = false;
            try
            {
                EnsureWritable();
                ArgumentNullException.ThrowIfNull(destination);
                if (!destination.CanWrite || ReferenceEquals(destination, this.destination))
                {
                    throw new ArgumentException("A segment requires a different writable destination.", nameof(destination));
                }
                EnsureSegmentBoundary();
                cancellationToken.ThrowIfCancellationRequested();
                await this.destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                this.destination = destination;
                segmentPending = true;
                success = true;
            }
            finally
            {
                FinishOperation(success);
            }
        }

        /// <summary>Captures the program state for a subsequent segment without performing I/O or retaining media buffers.
        /// The snapshot can be reused after this writer is disposed. It does not establish an AVC IDR boundary.</summary>
        public MpegTsSegmentState CaptureSegmentState()
        {
            Enter();
            try
            {
                EnsureWritable();
                EnsureSegmentBoundary();
                MpegTsTrackState[] states = tracks.Select(track =>
                    new MpegTsTrackState(track.Stream, track.Counter, track.LastDecodeTime)).ToArray();
                MpegTsProgramState program = new(lastDecodeTime!.Value, lastPcr, lastTables, patCounter, pmtCounter);
                return new(options, states, program);
            }
            finally
            {
                Volatile.Write(ref busy, 0);
            }
        }

        /// <summary>Continues a captured program in a borrowed stream using the snapshot's configuration and write limits.
        /// The first packet must belong to the clock track and follow the captured decode timeline.
        /// AVC requires an IDR access unit; audio retains the preceding segment's decoder history.</summary>
        public static MpegTsWriter ResumeSegment(Stream destination, MpegTsSegmentState state, MediaBufferAllocator allocator)
        {
            ArgumentNullException.ThrowIfNull(state);
            MpegTsWriter writer = Create(destination, state.Tracks.Select(track => track.Stream).ToArray(), allocator, state.Options);
            for (int index = 0; index < writer.tracks.Length; index++)
            {
                writer.tracks[index].Counter = state.Tracks[index].Counter;
                writer.tracks[index].LastDecodeTime = state.Tracks[index].LastDecodeTime;
            }
            writer.lastDecodeTime = state.Program.LastDecodeTime;
            writer.lastPcr = state.Program.LastPcr;
            writer.lastTables = state.Program.LastTables;
            writer.patCounter = state.Program.PatCounter;
            writer.pmtCounter = state.Program.PmtCounter;
            writer.segmentPending = true;
            return writer;
        }

        private void EnsureSegmentBoundary()
        {
            if (!lastDecodeTime.HasValue || segmentPending || (tracks.Length != 1 && tracks.Count(track => track.Type == 0x1B) != 1))
            {
                throw new InvalidOperationException("A segment boundary requires a nonempty program with one audio track or exactly one AVC track.");
            }
        }
    }
}
