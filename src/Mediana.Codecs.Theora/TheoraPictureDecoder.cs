// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Theora
{
    internal class TheoraPictureDecoder : IDisposable
    {
        private readonly MediaBufferAllocator allocator;
        private readonly TheoraSetup setup;
        private readonly TheoraFrameState state;
        internal TheoraLayout Layout { get; }
        private MediaBuffer? previous, golden;

        internal TheoraPictureDecoder(TheoraConfiguration configuration, MediaBufferAllocator allocator, CancellationToken token = default)
        {
            this.allocator = allocator; setup = TheoraSetup.Read(configuration.SetupHeader.Span, token);
            Layout = new(configuration.Identification, allocator); bool success = false;
            try { state = new(Layout, allocator); success = true; }
            finally { if (!success) { Layout.Dispose(); } }
        }

        internal MediaBuffer Decode(ReadOnlySpan<byte> packet, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (packet.IsEmpty) { return previous?.Retain() ?? throw new InvalidDataException("A Theora duplicate frame has no preceding picture."); }
            Span<int> qualities = stackalloc int[3]; TheoraBitReader reader = new(packet);
            bool inter = TheoraFrameSyntax.Read(ref reader, Layout, setup, state, qualities, out _, token);
            if (inter && previous is null) { throw new InvalidDataException("Theora decoding must begin with an intra frame."); }
            TheoraDcPrediction.Restore(Layout, state, token);
            using WritableMediaBuffer output = allocator.Allocate(Layout.SampleCount);
            TheoraReconstruction.Reconstruct(Layout, setup, state, qualities, output.Span,
                previous is null ? default : previous.Memory.Span, golden is null ? default : golden.Memory.Span, token);
            TheoraLoopFilter.Apply(Layout, state, output.Span, setup.FilterLimits[qualities[0]], token);
            MediaBuffer picture = output.Publish(); bool transferred = false;
            try
            {
                MediaBuffer retained = picture.Retain(); previous?.Dispose(); previous = retained;
                if (!inter) { retained = picture.Retain(); golden?.Dispose(); golden = retained; }
                transferred = true; return picture;
            }
            finally { if (!transferred) { picture.Dispose(); } }
        }

        public void Dispose() { previous?.Dispose(); golden?.Dispose(); previous = golden = null; state.Dispose(); Layout.Dispose(); }
    }
}
