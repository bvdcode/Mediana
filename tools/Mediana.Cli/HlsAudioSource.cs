// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Timing;

namespace Mediana.Cli
{
    internal abstract class HlsAudioSource : IAsyncDisposable
    {
        private MediaPacket? pending;
        private bool disposed;
        public abstract EncodedStreamInfo Stream { get; }
        public long PacketsWritten { get; private set; }

        protected abstract ValueTask<MediaPacket?> ReadPacketAsync(CancellationToken token);
        protected abstract ValueTask DisposeCoreAsync();

        public async ValueTask WriteBeforeAsync(MediaTimestamp? boundary, IReadOnlyList<HlsVideoRendition> renditions, CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                pending ??= await ReadPacketAsync(token);
                if (pending is null || (boundary is MediaTimestamp limit && pending.DecodeTime!.Value.CompareTo(limit) >= 0))
                {
                    return;
                }
                foreach (HlsVideoRendition rendition in renditions)
                {
                    await rendition.Writer.WritePacketAsync(pending, token);
                }
                pending.Dispose();
                pending = null;
                PacketsWritten++;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                pending?.Dispose();
                pending = null;
                await DisposeCoreAsync();
            }
            GC.SuppressFinalize(this);
        }
    }
}
