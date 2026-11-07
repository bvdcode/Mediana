// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Containers.Ogg
{
    internal class OggReadCursor(IRandomAccessByteSource source, long start = 0) : IByteSource
    {
        private long position = start;
        public long? Length => source.Length;

        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            int count = await source.ReadAtAsync(position, destination, cancellationToken).ConfigureAwait(false);
            if (count < 0 || count > destination.Length) { throw new InvalidDataException("The source returned an invalid Ogg range read length."); }
            position = checked(position + count);
            return count;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
