// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Streaming.Hls;

namespace Mediana.Tests
{
    internal class HlsPackageTestOutput : IHlsPackageOutput
    {
        internal Dictionary<(string Rendition, int Number), MemoryStream> Segments { get; } = [];
        internal Dictionary<string, string> Playlists { get; } = [];
        internal bool FailOnSegment { get; init; }
        internal Action? BeforeSegment { get; init; }

        public ValueTask<Stream> OpenSegmentAsync(string rendition, int number, CancellationToken cancellationToken)
        {
            BeforeSegment?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (FailOnSegment) { throw new IOException("segment-output"); }
            MemoryStream output = new();
            Segments.Add((rendition, number), output);
            return ValueTask.FromResult<Stream>(output);
        }

        public ValueTask WritePlaylistAsync(string relativeUri, string playlist, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Playlists.Add(relativeUri, playlist);
            return ValueTask.CompletedTask;
        }
    }
}
