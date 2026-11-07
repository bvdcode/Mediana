// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Audio;

namespace Mediana.Containers
{
    /// <summary>Writes decoded audio frames in one declared format. Borrows frames until each call completes.
    /// Only CompleteAsync finalizes the output; failed or unfinished output must be discarded.</summary>
    public interface IAudioFrameWriter : IAsyncDisposable
    {
        ValueTask WriteFrameAsync(AudioFrame frame, CancellationToken cancellationToken = default);
        ValueTask CompleteAsync(CancellationToken cancellationToken = default);
    }
}
