// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal class WebpByteBuffer(MediaBufferAllocator allocator) : IDisposable
    {
        private const int BlockSize = 4096;
        private readonly List<WritableMediaBuffer> blocks = [];
        internal int Length { get; private set; }

        internal byte this[int index]
        {
            get => blocks[index / BlockSize].Memory.Span[index % BlockSize];
            set => blocks[index / BlockSize].Memory.Span[index % BlockSize] = value;
        }

        internal void Add(byte value)
        {
            int offset = Length % BlockSize;
            if (offset == 0) { blocks.Add(allocator.Allocate(BlockSize)); }
            blocks[^1].Memory.Span[offset] = value;
            Length = checked(Length + 1);
        }

        internal void CopyTo(Span<byte> destination)
        {
            for (int index = 0; index < blocks.Count; index++)
            {
                int offset = index * BlockSize;
                blocks[index].Memory.Span[..Math.Min(BlockSize, Length - offset)].CopyTo(destination[offset..]);
            }
        }

        public void Dispose()
        {
            foreach (WritableMediaBuffer block in blocks) { block.Dispose(); }
            blocks.Clear();
        }
    }
}
