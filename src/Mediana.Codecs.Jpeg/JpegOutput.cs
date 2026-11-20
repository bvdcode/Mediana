// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Jpeg
{
    internal class JpegOutput(Stream destination, Memory<byte> storage)
    {
        private int count;
        private ulong pending;
        private int pendingBits;
        internal long Written { get; private set; }
        internal bool CanWriteMcu => storage.Length - count >= 4096;

        internal void Byte(int value) => storage.Span[count++] = (byte)value;
        internal void Word(int value) { Byte(value >> 8); Byte(value); }
        internal void Marker(int marker) { Byte(255); Byte(marker); }
        internal void Bytes(ReadOnlySpan<byte> values)
        {
            values.CopyTo(storage.Span[count..]);
            count += values.Length;
        }

        internal void Bits(uint value, int length)
        {
            pending = (pending << length) | (value & ((1u << length) - 1));
            pendingBits += length;
            Span<byte> bytes = storage.Span;
            while (pendingBits >= 8)
            {
                pendingBits -= 8;
                int octet = (int)(pending >> pendingBits) & 255;
                bytes[count++] = (byte)octet;
                if (octet == 255) { bytes[count++] = 0; }
            }
        }

        internal void FinishEntropy()
        {
            if (pendingBits != 0) { Bits(127, 8 - pendingBits); }
        }

        internal async Task FlushAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await destination.WriteAsync(storage[..count], cancellationToken).ConfigureAwait(false);
            Written += count;
            count = 0;
        }
    }
}
