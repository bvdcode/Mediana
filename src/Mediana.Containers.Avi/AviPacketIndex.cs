// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Containers.Avi
{
    internal class AviPacketIndex(MediaBufferAllocator allocator) : IDisposable
    {
        private const int EntriesPerPage = 256;
        private static readonly int EntryBytes = Unsafe.SizeOf<AviPacketEntry>();
        private readonly List<WritableMediaBuffer> pages = [];
        public long Count { get; private set; }

        public AviPacketEntry this[long index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
                return MemoryMarshal.Read<AviPacketEntry>(pages[checked((int)(index / EntriesPerPage))].Span
                    .Slice((int)(index % EntriesPerPage) * EntryBytes, EntryBytes));
            }
        }

        public void ExtendLastDuration(long duration)
        {
            AviPacketEntry entry = this[Count - 1];
            entry = entry with { Duration = checked(entry.Duration + duration) };
            MemoryMarshal.Write(pages[checked((int)((Count - 1) / EntriesPerPage))].Span
                .Slice((int)((Count - 1) % EntriesPerPage) * EntryBytes, EntryBytes), in entry);
        }

        public void Add(AviPacketEntry entry)
        {
            if (Count % EntriesPerPage == 0)
            {
                WritableMediaBuffer page = allocator.Allocate(EntryBytes * EntriesPerPage);
                bool added = false;
                try { pages.Add(page); added = true; }
                finally { if (!added) { page.Dispose(); } }
            }
            MemoryMarshal.Write(pages[checked((int)(Count / EntriesPerPage))].Span
                .Slice((int)(Count % EntriesPerPage) * EntryBytes, EntryBytes), in entry);
            Count = checked(Count + 1);
        }

        public void Dispose()
        {
            foreach (WritableMediaBuffer page in pages) { page.Dispose(); }
            pages.Clear();
            Count = 0;
        }
    }
}
