// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Tests
{
    internal class InterruptedUpdateStream(byte[] payload) : MemoryStream(payload)
    {
        public bool IsDisposed { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position == Length)
            {
                return ValueTask.FromException<int>(new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely."));
            }
            return base.ReadAsync(buffer, cancellationToken);
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            await base.CopyToAsync(destination, bufferSize, cancellationToken);
            throw new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely.");
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
