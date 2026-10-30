// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;
using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal class H264BitWriter : IDisposable
    {
        private readonly MediaBufferAllocator allocator;
        private readonly int maximumBytes;
        private readonly int prefixBytes;
        private WritableMediaBuffer storage;
        private int length;
        private int zeroBytes;
        private int partial;
        private int partialBits;
        private bool finished;

        public H264BitWriter(MediaBufferAllocator allocator, int maximumBytes, byte header, bool lengthPrefixed = false)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 8);
            this.allocator = allocator;
            this.maximumBytes = maximumBytes;
            prefixBytes = lengthPrefixed ? 4 : 0;
            storage = allocator.Allocate(Math.Min(4096, maximumBytes));
            length = prefixBytes;
            storage.Memory.Span[length++] = header;
        }

        public void WriteBits(ulong value, int count)
        {
            ObjectDisposedException.ThrowIf(finished, this);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 64);
            if (count < 64 && value >> count != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            for (int bit = count - 1; bit >= 0; bit--)
            {
                partial = (partial << 1) | (int)((value >> bit) & 1);
                if (++partialBits == 8)
                {
                    WriteByte((byte)partial);
                    partial = 0;
                    partialBits = 0;
                }
            }
        }

        public void WriteFlag(bool value) => WriteBits(value ? 1UL : 0, 1);

        public void WriteUnsigned(uint value)
        {
            ulong code = (ulong)value + 1;
            int bits = 64 - BitOperations.LeadingZeroCount(code);
            WriteBits(0, bits - 1);
            WriteBits(code, bits);
        }

        public void WriteSigned(int value)
        {
            long code = value <= 0 ? -(long)value * 2 : (long)value * 2 - 1;
            if (code > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            WriteUnsigned((uint)code);
        }

        public MediaBuffer Finish()
        {
            WriteFlag(true);
            if (partialBits != 0)
            {
                WriteBits(0, 8 - partialBits);
            }
            if (prefixBytes != 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(storage.Memory.Span, (uint)(length - prefixBytes));
            }
            finished = true;
            using MediaBuffer complete = storage.Publish();
            return complete.Slice(0, length);
        }

        private void WriteByte(byte value)
        {
            if (zeroBytes == 2 && value <= 3)
            {
                Append(3);
                zeroBytes = 0;
            }
            Append(value);
            zeroBytes = value == 0 ? zeroBytes + 1 : 0;
        }

        private void Append(byte value)
        {
            if (length == maximumBytes)
            {
                throw new NotSupportedException("The encoded H.264 packet exceeds the configured byte limit.");
            }
            if (length == storage.Memory.Length)
            {
                WritableMediaBuffer expanded = allocator.Allocate((int)Math.Min(maximumBytes, (long)length * 2));
                storage.Memory.Span[..length].CopyTo(expanded.Memory.Span);
                storage.Dispose();
                storage = expanded;
            }
            storage.Memory.Span[length++] = value;
        }

        public void Dispose()
        {
            finished = true;
            storage.Dispose();
        }
    }
}
