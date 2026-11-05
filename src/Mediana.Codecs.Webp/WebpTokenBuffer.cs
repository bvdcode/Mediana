// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal class WebpTokenBuffer(MediaBufferAllocator allocator) : IDisposable
    {
        private const int BlockTokens = 4096;
        private readonly List<WritableMediaBuffer> blocks = [];
        internal int Count { get; private set; }

        internal void Add(ulong token)
        {
            int offset = Count % BlockTokens;
            if (offset == 0) { blocks.Add(allocator.Allocate(BlockTokens * sizeof(ulong))); }
            MemoryMarshal.Cast<byte, ulong>(blocks[^1].Memory.Span)[offset] = token;
            Count++;
        }

        internal int BlockCount => blocks.Count;
        internal ReadOnlySpan<ulong> GetBlock(int block) => MemoryMarshal.Cast<byte, ulong>(blocks[block].Memory.Span)
            [..Math.Min(BlockTokens, Count - block * BlockTokens)];

        public void Dispose()
        {
            foreach (WritableMediaBuffer block in blocks) { block.Dispose(); }
            blocks.Clear();
        }
    }
}
