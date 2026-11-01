// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using Mediana.Core.Memory;

namespace Mediana.Containers.Matroska
{
    /// <summary>Append-only native index storage. Pages share one cumulative index budget.
    /// Operations and disposal are sequential; values contain no managed references.</summary>
    internal class MatroskaIndexTable<T>(MatroskaIndexBudget budget) : IDisposable where T : unmanaged
    {
        private const int PageBytes = 4096;
        private static readonly int EntryBytes = Unsafe.SizeOf<T>();
        private static readonly int MaximumPageEntries = 1 << BitOperations.Log2((uint)Math.Max(1, PageBytes / EntryBytes));
        private static readonly int GrowthPages = BitOperations.Log2((uint)MaximumPageEntries) + 1;
        private readonly List<WritableMediaBuffer> pages = [];
        private int usedInLastPage;
        private bool disposed;

        public int Count { get; private set; }

        public void Add(T value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Count == int.MaxValue)
            {
                throw new NotSupportedException("The Matroska index table has too many entries.");
            }
            if (pages.Count == 0 || usedInLastPage == pages[^1].Memory.Length / EntryBytes)
            {
                int requested = pages.Count < GrowthPages ? 1 << pages.Count : MaximumPageEntries;
                int capacity = Math.Min(requested, budget.RemainingBytes / EntryBytes);
                if (capacity == 0)
                {
                    throw new NotSupportedException("The Matroska packet index exceeds its configured byte limit.");
                }
                WritableMediaBuffer page = budget.Allocate(capacity * EntryBytes);
                bool stored = false;
                try
                {
                    pages.Add(page);
                    stored = true;
                }
                finally
                {
                    if (!stored)
                    {
                        page.Dispose();
                    }
                }
                usedInLastPage = 0;
            }
            MemoryMarshal.Cast<byte, T>(pages[^1].Memory.Span)[usedInLastPage] = value;
            usedInLastPage++;
            Count++;
        }

        public T Get(int index)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            int prefixEntries = MaximumPageEntries * 2 - 1;
            int page;
            int offset;
            if (index < prefixEntries)
            {
                page = BitOperations.Log2((uint)index + 1);
                offset = index - ((1 << page) - 1);
            }
            else
            {
                int tail = index - prefixEntries;
                page = GrowthPages + tail / MaximumPageEntries;
                offset = tail % MaximumPageEntries;
            }
            return MemoryMarshal.Cast<byte, T>(pages[page].Memory.Span)[offset];
        }

        public void Dispose()
        {
            disposed = true;
            foreach (WritableMediaBuffer page in pages)
            {
                page.Dispose();
            }
            pages.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
