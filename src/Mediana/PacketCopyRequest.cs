// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana
{
    public class PacketCopyRequest(int inputStreamIndex, int outputStreamIndex) : MediaTrackRequest(inputStreamIndex, outputStreamIndex)
    {
        internal override ValueTask<ProcessedMediaTrack> OpenAsync(MediaEngine engine, MediaTrackInput input, CancellationToken token) =>
            engine.OpenCopyTrackAsync(this, input, token);
    }
}
