// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Avi
{
    public partial class AviReader
    {
        private async ValueTask ReadVisualClocksAsync(AviTrack track, CancellationToken token)
        {
            using AviVisualIndexBuilder builder = new(source, allocator, track);
            await builder.BuildAsync(token).ConfigureAwait(false);
        }
    }
}
