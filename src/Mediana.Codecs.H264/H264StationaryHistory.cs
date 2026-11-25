// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    // Only a zero-motion block that the encoder actually skipped can start a run on the next picture.
    internal class H264StationaryHistory(int columns, int rows, MediaBufferAllocator allocator) : IDisposable
    {
        private readonly int count = checked(columns * rows);
        private readonly WritableMediaBuffer storage = allocator.Allocate(checked(columns * rows * (H264SourceMacroblock.ByteCount + 1)));

        public void Reset() => storage.Span[..count].Clear();

        public bool HasStationaryLeft(int address) => storage.Span[address - 1] != 0;

        public int MatchingRun(H264SourcePicture picture, int address, int x, int y)
        {
            if (storage.Span[address] == 0) { return 0; }
            int remaining = columns - x / 16;
            int firstInvalid = storage.Span.Slice(address, remaining).IndexOf((byte)0);
            if (firstInvalid >= 0) { remaining = firstInvalid; }
            for (int plane = 0; plane < 3 && remaining != 0; plane++)
            {
                int size = plane == 0 ? 16 : 8;
                remaining = picture.MatchingBlocks(plane, x / (16 / size), y / (16 / size), size, remaining,
                    PlaneStart(address, plane), columns * size);
            }
            return remaining;
        }

        public void Record(int address, ReadOnlySpan<byte> current, bool stationary)
        {
            storage.Span[address] = stationary ? (byte)1 : (byte)0;
            if (!stationary) { return; }
            for (int plane = 0; plane < 3; plane++)
            {
                int size = plane == 0 ? 16 : 8;
                Span<byte> prior = PlaneStart(address, plane);
                int offset = plane == 0 ? 0 : 256 + (plane - 1) * 64;
                for (int row = 0; row < size; row++)
                {
                    current.Slice(offset + row * size, size).CopyTo(prior[(row * columns * size)..]);
                }
            }
        }

        private Span<byte> PlaneStart(int address, int plane)
        {
            int size = plane == 0 ? 16 : 8;
            int planeOffset = plane == 0 ? 0 : columns * (256 + (plane - 1) * 64);
            int rowOffset = address / columns * columns * H264SourceMacroblock.ByteCount;
            return storage.Span[(count + rowOffset + planeOffset + address % columns * size)..];
        }

        public void Dispose() => storage.Dispose();
    }
}
