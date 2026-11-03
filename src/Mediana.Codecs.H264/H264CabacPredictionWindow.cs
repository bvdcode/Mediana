// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using System.Runtime.InteropServices;

namespace Mediana.Codecs.H264
{
    internal class H264CabacPredictionWindow : IDisposable
    {
        private const int BlocksPerMacroblock = 4;
        private readonly WritableMediaBuffer storage;
        private readonly int columns;
        private readonly int blocks;
        private readonly int rowMask;
        private readonly int listBytes;
        private readonly bool bidirectional;
        private int currentRow = -1;

        public H264CabacPredictionWindow(int width, int height, bool bidirectional, MediaBufferAllocator allocator)
        {
            if (width <= 0 || height <= 0 || ((width | height) & 15) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "CABAC history requires complete progressive macroblocks.");
            }
            this.bidirectional = bidirectional;
            columns = width / 4;
            int rows = Math.Min(2, height / 16) * BlocksPerMacroblock;
            rowMask = rows - 1;
            blocks = checked(columns * rows);
            listBytes = checked(blocks * (sizeof(short) * 2 + (bidirectional ? 1 : 0)));
            storage = allocator.Allocate(checked(listBytes * (bidirectional ? 2 : 1)));
        }

        public Span<short> MotionX(H264ReferenceList list) =>
            MemoryMarshal.Cast<byte, short>(storage.Span.Slice(ListOffset(list), blocks * sizeof(short)));

        public Span<short> MotionY(H264ReferenceList list) =>
            MemoryMarshal.Cast<byte, short>(storage.Span.Slice(ListOffset(list) + blocks * sizeof(short), blocks * sizeof(short)));

        public Span<byte> ReferenceConditions(H264ReferenceList list)
        {
            if (!bidirectional)
            {
                throw new InvalidOperationException("Unidirectional reference contexts use reconstructed picture indices.");
            }
            return storage.Span.Slice(ListOffset(list) + blocks * sizeof(short) * 2, blocks);
        }

        public int BlockIndex(int x, int y) => ((y / 4) & rowMask) * columns + x / 4;

        public void BeginMacroblock(int address)
        {
            int row = address / (columns / BlocksPerMacroblock);
            if (row == currentRow)
            {
                return;
            }
            int start = ((row * BlocksPerMacroblock) & rowMask) * columns;
            int count = columns * BlocksPerMacroblock;
            Clear(H264ReferenceList.L0, start, count);
            if (bidirectional)
            {
                Clear(H264ReferenceList.L1, start, count);
            }
            currentRow = row;
        }

        private void Clear(H264ReferenceList list, int start, int count)
        {
            MotionX(list).Slice(start, count).Clear();
            MotionY(list).Slice(start, count).Clear();
            if (bidirectional)
            {
                ReferenceConditions(list).Slice(start, count).Clear();
            }
        }

        private int ListOffset(H264ReferenceList list) => list switch
        {
            H264ReferenceList.L0 => 0,
            H264ReferenceList.L1 when bidirectional => listBytes,
            H264ReferenceList.L1 => throw new InvalidOperationException("The CABAC picture has no list 1 syntax state."),
            _ => throw new ArgumentOutOfRangeException(nameof(list))
        };

        public void Dispose() => storage.Dispose();
    }
}
