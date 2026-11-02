// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Numerics;
using Mediana.Core.Memory;

namespace Mediana.Formats.Video
{
    /// <summary>Writes a budgeted NAL with emulation prevention, RBSP trailing bits and optional AVC length prefix.</summary>
    public class H264BitWriter : IDisposable
    {
        private readonly MediaBufferAllocator allocator;
        private readonly int maximumBytes;
        private readonly int prefixBytes;
        private readonly bool measureOverflow;
        private WritableMediaBuffer storage;
        private int length;
        private int zeroBytes;
        private int partial;
        private int partialBits;
        private int preventionBytes;
        private int revision;
        private bool finished;

        public H264BitWriter(MediaBufferAllocator allocator, int maximumBytes, byte header, bool lengthPrefixed = false, bool measureOverflow = false)
        {
            ArgumentNullException.ThrowIfNull(allocator);
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 8);
            this.allocator = allocator;
            this.maximumBytes = maximumBytes;
            this.measureOverflow = measureOverflow;
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
            while (count > 0)
            {
                int take = Math.Min(8 - partialBits, count);
                count -= take;
                partial = (partial << take) | (int)((value >> count) & ((1UL << take) - 1));
                partialBits += take;
                if (partialBits == 8)
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

        public int EncodedLength => length;

        /// <summary>Gets the RBSP bit position, excluding the NAL header, length prefix and emulation-prevention bytes.</summary>
        public long BitPosition => ((long)length - prefixBytes - 1 - preventionBytes) * 8 + partialBits;

        /// <summary>Captures the current unfinished NAL without copying its storage.</summary>
        public H264BitWriterCheckpoint CreateCheckpoint()
        {
            ObjectDisposedException.ThrowIf(finished, this);
            return new(this, revision, length, zeroBytes, partial, partialBits, preventionBytes, BitPosition);
        }

        /// <summary>Discards writes after a checkpoint and invalidates all previously captured checkpoints.</summary>
        public void Restore(in H264BitWriterCheckpoint checkpoint)
        {
            ObjectDisposedException.ThrowIf(finished, this);
            if (!ReferenceEquals(checkpoint.Writer, this) || checkpoint.Revision != revision)
            {
                throw new ArgumentException("The checkpoint does not belong to this NAL revision.", nameof(checkpoint));
            }
            revision = checked(revision + 1);
            length = checkpoint.Length;
            zeroBytes = checkpoint.ZeroBytes;
            partial = checkpoint.Partial;
            partialBits = checkpoint.PartialBits;
            preventionBytes = checkpoint.PreventionBytes;
        }

        public MediaBuffer Finish() => TryFinish()
            ?? throw new NotSupportedException("The encoded H.264 packet exceeds the configured byte limit.");

        /// <summary>Finishes the NAL; returns null when measured output exceeded storage capacity.</summary>
        public MediaBuffer? TryFinish()
        {
            WriteFlag(true);
            if (partialBits != 0)
            {
                WriteBits(0, 8 - partialBits);
            }
            finished = true;
            if (length > maximumBytes)
            {
                return null;
            }
            if (prefixBytes != 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(storage.Memory.Span, (uint)(length - prefixBytes));
            }
            using MediaBuffer complete = storage.Publish();
            return complete.Slice(0, length);
        }

        private void WriteByte(byte value)
        {
            if (zeroBytes == 2 && value <= 3)
            {
                Append(3);
                preventionBytes++;
                zeroBytes = 0;
            }
            Append(value);
            zeroBytes = value == 0 ? zeroBytes + 1 : 0;
        }

        private void Append(byte value)
        {
            if (length >= maximumBytes)
            {
                if (!measureOverflow)
                {
                    throw new NotSupportedException("The encoded H.264 packet exceeds the configured byte limit.");
                }
                length = checked(length + 1);
                return;
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
