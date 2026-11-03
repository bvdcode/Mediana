// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mediana.Tests
{
    internal static class WritableMediaSpanTestInput
    {
        public static void VerifyPublication()
        {
            MediaBufferAllocator allocator = new(16);
            using WritableMediaBuffer writable = allocator.Allocate(16);
            if (writable.Span.Length != 16 || writable.Span.IndexOfAnyExcept((byte)0) >= 0
                || !Unsafe.AreSame(ref MemoryMarshal.GetReference(writable.Span), ref MemoryMarshal.GetReference(writable.Memory.Span)))
            {
                throw new InvalidDataException("Writable span access changed the allocation or its zero initialization.");
            }
            writable.Span.Fill(31);
            writable.Span[7] = 42;
            writable.Memory.Span[8] = 43;
            if (writable.Memory.Span[7] != 42 || writable.Span[8] != 43 || allocator.AllocatedBytes != 16)
            {
                throw new InvalidDataException("Writable views do not share their owned storage.");
            }
            using MediaBuffer published = writable.Publish();
            using MediaBuffer retained = published.Slice(7, 2);
            ExpectDisposed(writable);
            writable.Dispose();
            published.Dispose();
            if (!retained.Memory.Span.SequenceEqual(new byte[] { 42, 43 }) || allocator.AllocatedBytes != 16)
            {
                throw new InvalidDataException("Publishing writable span storage invalidated an independent lease.");
            }
            retained.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Writable span publication retained the allocation budget.");
            }
        }

        public static void VerifyDisposal()
        {
            MediaBufferAllocator allocator = new(16);
            using WritableMediaBuffer writable = allocator.Allocate(16);
            writable.Dispose();
            ExpectDisposed(writable);
            writable.Dispose();
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Writable span disposal did not release its allocation exactly once.");
            }
        }

        public static void VerifyAllocation()
        {
            MediaBufferAllocator allocator = new(64);
            using (WritableMediaBuffer writable = allocator.Allocate(64))
            {
                writable.Span.Fill(37);
                Repeat(writable);
                long before = GC.GetAllocatedBytesForCurrentThread();
                int sum = Repeat(writable);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (allocated != 0 || sum != 37 * 4096 || allocator.AllocatedBytes != 64)
                {
                    throw new InvalidDataException($"Synchronous span access allocated {allocated} bytes or changed its contents.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidDataException("Synchronous span access retained its allocation budget.");
            }
        }

        private static int Repeat(WritableMediaBuffer writable)
        {
            int sum = 0;
            for (int index = 0; index < 4096; index++)
            {
                sum += writable.Span[index % 64];
            }
            return sum;
        }

        private static void ExpectDisposed(WritableMediaBuffer writable)
        {
            bool disposed = false;
            try
            {
                _ = writable.Span.Length;
            }
            catch (ObjectDisposedException error)
            {
                disposed = error.ObjectName == nameof(WritableMediaBuffer);
            }
            if (!disposed)
            {
                throw new InvalidDataException("A released writable owner exposed its span.");
            }
        }
    }
}
