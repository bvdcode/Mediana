// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.IO
{
    /// <summary>Adapts a readable, seekable stream. Offsets start at its initial position.
    /// The caller must not access the underlying stream while the source is in use.</summary>
    public class RandomAccessStreamByteSource(Stream stream, bool leaveOpen = false)
        : StreamByteSource(stream, leaveOpen), IRandomAccessByteSource
    {
        private readonly Stream innerStream = Validate(stream);
        private readonly long origin = stream.Position;

        public async ValueTask<int> ReadAtAsync(long offset, Memory<byte> destination,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            cancellationToken.ThrowIfCancellationRequested();
            long length = Length!.Value;
            if (destination.IsEmpty || offset >= length)
            {
                return 0;
            }

            int count = (int)Math.Min(destination.Length, length - offset);
            long position = innerStream.Position;
            innerStream.Seek(checked(origin + offset), SeekOrigin.Begin);
            try
            {
                return await innerStream.ReadAsync(destination[..count], cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                innerStream.Seek(position, SeekOrigin.Begin);
            }
        }

        private static Stream Validate(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanSeek)
            {
                throw new ArgumentException("The stream must support seeking.", nameof(stream));
            }
            return stream;
        }
    }
}
