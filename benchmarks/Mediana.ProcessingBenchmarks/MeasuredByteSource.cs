// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.ProcessingBenchmarks
{
    internal class MeasuredByteSource(IRandomAccessByteSource source) : IRandomAccessByteSource
    {
        public long BytesRead { get; private set; }
        public long Reads { get; private set; }
        public long? Length => source.Length;
        public async ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            int count = await source.ReadAtAsync(offset, destination, cancellationToken).ConfigureAwait(false);
            BytesRead = checked(BytesRead + count);
            Reads++;
            return count;
        }

        public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            int count = await source.ReadAsync(destination, cancellationToken).ConfigureAwait(false);
            BytesRead = checked(BytesRead + count);
            Reads++;
            return count;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
