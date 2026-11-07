// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Timing;

namespace Mediana.Containers
{
    /// <summary>Optional explicit presentation selection on the reader's packet timeline.
    /// Null means no container-declared selection; it does not infer codec delay or padding.
    /// Queries preserve cursors and do not read encoded payload. Unsupported nonlinear selections throw.</summary>
    public interface IContainerPresentationReader : IContainerReader
    {
        MediaTimeRange? GetPresentationRange(int streamIndex, CancellationToken cancellationToken = default);
    }
}
