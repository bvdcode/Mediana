// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;
using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal class WebpTokenBuffer(MediaBufferAllocator allocator) : IDisposable
    {
        private const int BlockWords = 4096;
        private const int FlagWords = BlockWords / 32;
        private const int HeaderWords = 1 + FlagWords;
        private readonly List<WritableMediaBuffer> blocks = [];

        internal void Add(ulong token)
        {
            bool reference = token >> 32 != 0;
            int required = reference ? 2 : 1;
            if (blocks.Count == 0 || MemoryMarshal.Cast<byte, uint>(blocks[^1].Memory.Span)[0] + required > BlockWords)
            {
                blocks.Add(allocator.Allocate((BlockWords + HeaderWords) * sizeof(uint)));
            }
            Span<uint> block = MemoryMarshal.Cast<byte, uint>(blocks[^1].Memory.Span);
            int offset = (int)block[0];
            block[HeaderWords + offset] = (uint)token;
            if (reference)
            {
                block[1 + offset / 32] |= 1u << (offset & 31);
                block[HeaderWords + offset + 1] = (uint)(token >> 32);
            }
            block[0] = (uint)(offset + required);
        }

        internal int BlockCount => blocks.Count;
        internal ReadOnlySpan<uint> GetBlock(int block)
        {
            ReadOnlySpan<uint> storage = MemoryMarshal.Cast<byte, uint>(blocks[block].Memory.Span);
            return storage.Slice(HeaderWords, (int)storage[0]);
        }

        internal ReadOnlySpan<uint> GetReferences(int block) => MemoryMarshal.Cast<byte, uint>(blocks[block].Memory.Span).Slice(1, FlagWords);

        public void Dispose()
        {
            foreach (WritableMediaBuffer block in blocks) { block.Dispose(); }
            blocks.Clear();
        }
    }
}
