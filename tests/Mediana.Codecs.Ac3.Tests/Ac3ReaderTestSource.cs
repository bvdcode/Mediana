// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Codecs.Ac3.Tests
{
    internal class Ac3ReaderTestSource(ReadOnlyMemory<byte> data) : IRandomAccessByteSource
    {
        public long? Length => data.Length;
        public int MaximumRead { get; init; } = int.MaxValue;
        public int ReadCalls { get; private set; }
        public long ReadBytes { get; private set; }
        public bool HeadersOnly { get; set; }
        public IReadOnlyList<(long Start, long End)>? AllowedReadRanges { get; set; }
        public bool FailNext { get; set; }
        public bool Disposed { get; private set; }
        public Task? Gate { get; init; }

        public async ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); ReadCalls++;
            if (Gate is not null) { await Gate.WaitAsync(cancellationToken); }
            if (FailNext) { FailNext = false; throw new IOException("The source read failed."); }
            if (HeadersOnly) { Assert.True(destination.Length <= 8); }
            if (AllowedReadRanges is not null)
            { Assert.Contains(AllowedReadRanges, range => offset >= range.Start && offset + destination.Length <= range.End); }
            if (offset >= data.Length) { return 0; }
            int count = Math.Min(Math.Min(destination.Length, MaximumRead), data.Length - (int)offset);
            data.Slice((int)offset, count).CopyTo(destination); ReadBytes += count; return count;
        }
        public ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The sequential source cursor must remain unchanged.");
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
