// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;
using Mediana.Execution;

namespace Mediana
{
    public class PacketCopyRequest : MediaTrackRequest
    {
        public PacketCopyRequest(int inputStreamIndex, int outputStreamIndex,
            PacketCopyAlignment alignment = PacketCopyAlignment.Exact, MediaTimeRange? selection = null)
            : base(inputStreamIndex, outputStreamIndex)
        {
            if (!Enum.IsDefined(alignment)) { throw new ArgumentOutOfRangeException(nameof(alignment)); }
            Alignment = alignment;
            Selection = selection;
        }

        public PacketCopyAlignment Alignment { get; }
        /// <summary>An explicit per-track interval. Null uses the operation's interval; no container edit is applied to copied payload.</summary>
        public MediaTimeRange? Selection { get; }
        internal override ValueTask<ProcessedMediaTrack> OpenAsync(MediaEngine engine, MediaTrackInput input, CancellationToken token) =>
            engine.OpenCopyTrackAsync(this, input, token);
    }
}
