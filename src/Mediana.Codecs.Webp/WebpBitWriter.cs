// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;

namespace Mediana.Codecs.Webp
{
    internal class WebpBitWriter(Memory<byte> destination = default)
    {
        private ulong pending;
        private int pendingBits;
        private int position;
        internal long BitCount { get; private set; }

        internal void Write(uint value, int count)
        {
            BitCount += count;
            if (destination.IsEmpty || count == 0) { return; }
            pending |= (ulong)value << pendingBits;
            pendingBits += count;
            if (pendingBits >= 32)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Span[position..], (uint)pending);
                position += 4;
                pending >>= 32;
                pendingBits -= 32;
            }
        }

        internal void Complete()
        {
            while (pendingBits > 0)
            {
                destination.Span[position++] = (byte)pending;
                pending >>= 8;
                pendingBits -= 8;
            }
            pending = 0;
            pendingBits = 0;
        }
    }
}
