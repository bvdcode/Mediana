// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana.Containers.Ogg
{
    internal class OggPageCache(IRandomAccessByteSource source, MediaBufferAllocator allocator, OggReadOptions options) : IDisposable
    {
        private OggPage? cached;

        public async ValueTask<OggPage> ReadAsync(long offset, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (cached is not null && cached.Offset == offset) { return cached.Retain(); }
            await using OggReadCursor cursor = new(source, offset);
            using OggPageReader pages = new(cursor, allocator, options, offset);
            OggPage loaded = await pages.ReadAsync(token).ConfigureAwait(false)
                ?? throw new InvalidDataException("The indexed Ogg page is missing.");
            cached?.Dispose();
            cached = loaded;
            return loaded.Retain();
        }

        public void Dispose()
        {
            cached?.Dispose();
            cached = null;
        }
    }
}
