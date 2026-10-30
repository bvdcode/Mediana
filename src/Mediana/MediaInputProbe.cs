// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.IO;

namespace Mediana
{
    internal static class MediaInputProbe
    {
        public static async ValueTask<int> ReadAsync(IByteSource source, Memory<byte> destination, CancellationToken cancellationToken)
        {
            int read = 0;
            while (read < destination.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = source is IRandomAccessByteSource randomSource
                    ? await randomSource.ReadAtAsync(read, destination[read..], cancellationToken).ConfigureAwait(false)
                    : await source.ReadAsync(destination[read..], cancellationToken).ConfigureAwait(false);
                if (count < 0 || count > destination.Length - read)
                {
                    throw new InvalidDataException("The source returned an invalid read length.");
                }
                if (count == 0)
                {
                    break;
                }
                read += count;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return read;
        }

        public static IByteSource Replay(IByteSource source, MediaBuffer prefix, int prefixLength) =>
            source is IRandomAccessByteSource randomAccess
                ? new RandomAccessCursorByteSource(randomAccess, leaveOpen: true)
                : new PrefixByteSource(source, prefix, prefixLength, leaveOpen: true);
    }
}
