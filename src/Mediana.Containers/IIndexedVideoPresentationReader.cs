// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers
{
    /// <summary>Optional displayed-picture index, independent of coded packet count and ordering.
    /// A decode-only packet has no presentation units; a packet may have several distinctly timed units.
    /// Queries preserve packet cursors, do not read encoded media, and do not transfer ownership.</summary>
    public interface IIndexedVideoPresentationReader : IIndexedPacketTimingReader
    {
        long GetPresentationUnitCount(int streamIndex);

        ContainerVideoPresentationUnit GetPresentationUnit(int streamIndex, long presentationUnitIndex,
            CancellationToken cancellationToken = default);
    }
}
