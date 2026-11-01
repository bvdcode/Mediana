// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Containers.Matroska;
using Mediana.Core.Memory;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaIndexTableTestInput
    {
        public static void VerifyPages()
        {
            MediaBufferAllocator allocator = new(131072);
            using (MatroskaIndexTable<long> table = new(new(allocator, 100000)))
            {
                for (int index = 0; index < 10000; index++)
                {
                    table.Add(-(long)index * index);
                }
                Require(table.Count == 10000 && allocator.AllocatedBytes >= 80000 && allocator.AllocatedBytes < 84100);
                for (int index = 9999; index >= 0; index--)
                {
                    Require(table.Get(index) == -(long)index * index);
                }
                for (int index = 0; index < 10000; index++)
                {
                    int shuffled = index * 7919 % 10000;
                    Require(table.Get(shuffled) == -(long)shuffled * shuffled);
                }
            }
            Require(allocator.AllocatedBytes == 0);
        }

        public static async Task VerifyBoundsAsync()
        {
            MediaBufferAllocator allocator = new(64);
            MatroskaIndexBudget budget = new(allocator, 49);
            MatroskaIndexTable<long> left = new(budget);
            MatroskaIndexTable<byte> right = new(budget);
            using (left)
            using (right)
            {
                left.Add(1);
                right.Add(17);
                left.Add(2);
                left.Add(3);
                left.Add(4);
                left.Add(5);
                left.Add(6);
                Require(allocator.AllocatedBytes == 49 && budget.RemainingBytes == 0);
                for (int index = 0; index < 6; index++)
                {
                    Require(left.Get(index) == index + 1);
                }
                Require(right.Get(0) == 17);
                await RejectAsync<NotSupportedException>(() => Task.Run(() => left.Add(7)));
                await RejectAsync<NotSupportedException>(() => Task.Run(() => right.Add(18)));
                await RejectAsync<ArgumentOutOfRangeException>(() => Task.Run(() => left.Get(-1)));
                await RejectAsync<ArgumentOutOfRangeException>(() => Task.Run(() => left.Get(6)));
            }
            Require(allocator.AllocatedBytes == 0);
            left.Dispose();
            await RejectAsync<ObjectDisposedException>(() => Task.Run(() => left.Add(1)));
            await RejectAsync<ObjectDisposedException>(() => Task.Run(() => left.Get(0)));
            MediaBufferAllocator small = new(7);
            using (MatroskaIndexTable<long> table = new(new(small, 64)))
            {
                await RejectAsync<MediaMemoryLimitException>(() => Task.Run(() => table.Add(1)));
                Require(table.Count == 0 && small.AllocatedBytes == 0);
            }
        }
    }
}
