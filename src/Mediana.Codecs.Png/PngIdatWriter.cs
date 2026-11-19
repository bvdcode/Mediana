// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers;
using System.IO.Compression;

namespace Mediana.Codecs.Png
{
    internal class PngIdatWriter(Stream destination, Memory<byte> chunk, CancellationToken cancellationToken)
    {
        private int _pending;
        public long WrittenBytes { get; private set; }

        internal async ValueTask WriteAsync(ZLibEncoder compressor, ReadOnlyMemory<byte> source, bool final)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OperationStatus status = compressor.Compress(source.Span, chunk.Span.Slice(8 + _pending, chunk.Length - 12 - _pending),
                    out int consumed, out int written, final);
                source = source[consumed..];
                _pending += written;
                if (_pending == chunk.Length - 12 || status == OperationStatus.DestinationTooSmall)
                {
                    if (_pending == 0) { throw new InvalidOperationException("The PNG compressor made no progress."); }
                    await WriteChunkAsync().ConfigureAwait(false);
                }
                switch (status)
                {
                    case OperationStatus.Done:
                        if (!source.IsEmpty) { throw new InvalidOperationException("The PNG compressor left unread input."); }
                        return;
                    case OperationStatus.DestinationTooSmall:
                        break;
                    case OperationStatus.NeedMoreData:
                    case OperationStatus.InvalidData:
                    default:
                        throw new InvalidOperationException("Unexpected PNG compressor status.");
                }
            }
        }

        internal async ValueTask CompleteAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_pending != 0) { await WriteChunkAsync().ConfigureAwait(false); }
        }

        private async ValueTask WriteChunkAsync()
        {
            "IDAT"u8.CopyTo(chunk.Span.Slice(4, 4));
            PngChunk.Finish(chunk.Span, _pending, cancellationToken);
            int count = _pending + 12;
            await destination.WriteAsync(chunk[..count], cancellationToken).ConfigureAwait(false);
            WrittenBytes = checked(WrittenBytes + count);
            _pending = 0;
        }
    }
}
