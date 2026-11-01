// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;

namespace Mediana.Tests
{
    internal static class BoundedStreamReadTestInput
    {
        private const int AllocationLength = 1_500_000;
        private const int Repetitions = 3;

        internal static void VerifySynchronous()
        {
            foreach ((byte[] expected, int maximumRead, int limit) in Cases())
            {
                byte[] input = expected.ToArray();
                using FragmentedReadStream source = new(input, maximumRead);
                byte[] result = BoundedStreamReader.Read(source, limit);
                VerifyResult(expected, input, result, source);
            }
        }

        internal static async Task VerifyAsync()
        {
            foreach ((byte[] expected, int maximumRead, int limit) in Cases())
            {
                byte[] input = expected.ToArray();
                using FragmentedReadStream source = new(input, maximumRead);
                byte[] result = await BoundedStreamReader.ReadAsync(source, limit);
                VerifyResult(expected, input, result, source);
                if (source.SyncReads != 0 || source.AsyncReads == 0)
                {
                    throw new InvalidDataException("Asynchronous bounded reads must use asynchronous source I/O.");
                }
            }
        }

        internal static void VerifyAllocationSynchronous()
        {
            byte[] input = Samples(AllocationLength);
            for (int warmup = 0; warmup < Repetitions; warmup++)
            {
                using MemoryStream source = new(input, writable: false);
                _ = BoundedStreamReader.Read(source, AllocationLength + 1);
            }
            long allocated = 0;
            for (int iteration = 0; iteration < Repetitions; iteration++)
            {
                using MemoryStream source = new(input, writable: false);
                long before = GC.GetTotalAllocatedBytes(precise: true);
                byte[] result = BoundedStreamReader.Read(source, AllocationLength + 1);
                allocated += GC.GetTotalAllocatedBytes(precise: true) - before;
                VerifyAllocationResult(input, result);
            }
            VerifyAllocated(allocated);
        }

        internal static async Task VerifyAllocationAsync()
        {
            byte[] input = Samples(AllocationLength);
            for (int warmup = 0; warmup < Repetitions; warmup++)
            {
                using MemoryStream source = new(input, writable: false);
                _ = await BoundedStreamReader.ReadAsync(source, AllocationLength + 1);
            }
            long allocated = 0;
            for (int iteration = 0; iteration < Repetitions; iteration++)
            {
                using MemoryStream source = new(input, writable: false);
                long before = GC.GetTotalAllocatedBytes(precise: true);
                byte[] result = await BoundedStreamReader.ReadAsync(source, AllocationLength + 1);
                allocated += GC.GetTotalAllocatedBytes(precise: true) - before;
                VerifyAllocationResult(input, result);
            }
            VerifyAllocated(allocated);
        }

        private static IEnumerable<(byte[] Expected, int MaximumRead, int Limit)> Cases()
        {
            foreach (int length in new[] { 0, 1, 19, 65535, 65536, 65537, 131087 })
            {
                byte[] expected = Samples(length);
                foreach (int maximumRead in new[] { 1, 19, 65536 })
                {
                    yield return (expected, maximumRead, length);
                    yield return (expected, maximumRead, length + 31);
                }
            }
        }

        private static void VerifyResult(byte[] expected, byte[] input, byte[] result, Stream source)
        {
            if (!result.AsSpan().SequenceEqual(expected) || !source.CanRead)
            {
                throw new InvalidDataException("Bounded reads changed bytes or closed their source.");
            }
            input.AsSpan().Fill(255);
            if (!result.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidDataException("Bounded results must survive source memory reuse.");
            }
            result.AsSpan().Fill(127);
            if (input.AsSpan().IndexOfAnyExcept((byte)255) >= 0)
            {
                throw new InvalidDataException("Bounded results must own independent exact-length storage.");
            }
        }

        private static void VerifyAllocationResult(byte[] input, byte[] result)
        {
            if (!result.AsSpan().SequenceEqual(input))
            {
                throw new InvalidDataException("Bounded allocation verification requires every input byte.");
            }
        }

        private static void VerifyAllocated(long allocated)
        {
            if (allocated >= (long)AllocationLength * Repetitions * 5 / 2)
            {
                throw new InvalidDataException("Bounded reads must not allocate geometric growth copies of the complete payload.");
            }
        }

        private static byte[] Samples(int length)
        {
            byte[] data = new byte[length];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i * 13 + i / 23);
            }
            return data;
        }
    }
}
