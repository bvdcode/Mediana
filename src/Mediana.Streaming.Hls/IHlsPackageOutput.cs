// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Streaming.Hls
{
    /// <summary>Destination for a complete rendition package. Writers own returned segment streams.
    /// Outputs may contain partial data on failure; publication transactions belong to the destination.</summary>
    public interface IHlsPackageOutput
    {
        ValueTask<Stream> OpenSegmentAsync(string rendition, int number, CancellationToken cancellationToken);
        ValueTask WritePlaylistAsync(string relativeUri, string playlist, CancellationToken cancellationToken);
    }
}
