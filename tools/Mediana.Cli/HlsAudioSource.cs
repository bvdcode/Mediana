// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers;
using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Cli
{
    internal abstract class HlsAudioSource : IAsyncDisposable
    {
        public abstract EncodedStreamInfo Stream { get; }
        public abstract long PacketsWritten { get; protected set; }
        public virtual EncodedStreamInfo GetStream(int variantIndex)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(variantIndex);
            return Stream;
        }
        public abstract ValueTask WriteBeforeAsync(MediaTimestamp? boundary,
            IReadOnlyList<IContainerWriter> destinations, CancellationToken token);
        public abstract ValueTask DisposeAsync();
    }
}
