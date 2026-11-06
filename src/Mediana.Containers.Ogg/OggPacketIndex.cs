// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Containers.Ogg
{
    internal class OggPacketIndex(MediaBufferAllocator allocator) : IDisposable
    {
        private const int EntriesPerPage = 128;
        private static readonly int EntryBytes = Unsafe.SizeOf<OggIndexedPacket>();
        private readonly List<WritableMediaBuffer> pages = [];
        public long Count { get; private set; }

        public OggIndexedPacket this[long index]
        {
            get => MemoryMarshal.Read<OggIndexedPacket>(Entry(index));
            set => MemoryMarshal.Write(Entry(index), in value);
        }

        public void Add(OggIndexedPacket entry)
        {
            if (Count % EntriesPerPage == 0)
            {
                WritableMediaBuffer page = allocator.Allocate(EntryBytes * EntriesPerPage);
                bool added = false;
                try { pages.Add(page); added = true; }
                finally { if (!added) { page.Dispose(); } }
            }
            long index = Count;
            Count = checked(Count + 1);
            this[index] = entry;
        }

        private Span<byte> Entry(long index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return pages[checked((int)(index / EntriesPerPage))].Span.Slice((int)(index % EntriesPerPage) * EntryBytes, EntryBytes);
        }

        public void Dispose()
        {
            foreach (WritableMediaBuffer page in pages) { page.Dispose(); }
            pages.Clear();
            Count = 0;
        }
    }
}
