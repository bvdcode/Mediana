// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class MeteredReadStream(byte[] data) : MemoryStream(data)
    {
        internal long BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer[..Math.Min(127, buffer.Length)], cancellationToken);
            BytesRead += read;
            return read;
        }
    }
}
