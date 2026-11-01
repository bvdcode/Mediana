// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Tests
{
    internal class EbmlElementTestSource(ReadOnlyMemory<byte> data) : IRandomAccessByteSource
    {
        public long? Length { get; init; } = data.Length;
        public int MaximumRead { get; init; } = int.MaxValue;
        public long BaseOffset { get; init; }
        public long ReadBoundary { get; set; } = long.MaxValue;
        public int? ReadCountOverride { get; init; }
        public Task? ReadGate { get; init; }
        public long ReadGateOffset { get; init; }
        public int ReadCalls { get; private set; }
        public bool IsDisposed { get; private set; }

        public async ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            if (ReadGate is not null && offset >= ReadGateOffset)
            {
                await ReadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (offset < BaseOffset || offset > ReadBoundary || destination.Length > ReadBoundary - offset)
            {
                throw new InvalidOperationException("An EBML range read crossed its permitted boundary.");
            }
            if (ReadCountOverride is int invalid)
            {
                return invalid;
            }
            long start = offset - BaseOffset;
            if (start >= data.Length)
            {
                return 0;
            }
            int count = Math.Min(Math.Min(destination.Length, MaximumRead), data.Length - (int)start);
            data.Slice((int)start, count).CopyTo(destination);
            return count;
        }

        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("An EBML header read moved the sequential source cursor.");

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
