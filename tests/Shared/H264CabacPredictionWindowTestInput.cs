// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs.H264;
using Mediana.Core.Memory;

namespace Mediana.Tests
{
    internal static class H264CabacPredictionWindowTestInput
    {
        public static void Verify()
        {
            foreach (bool bidirectional in new[] { false, true })
            {
                foreach (int columns in new[] { 1, 3, 8 })
                {
                    foreach (int rows in new[] { 1, 2, 3, 5, 23 })
                    {
                        foreach (int start in new[] { 0, 1, columns + 1 })
                        {
                            if (start < columns * rows)
                            {
                                VerifyPicture(columns, rows, start, bidirectional);
                            }
                        }
                    }
                }
            }
        }

        private static void VerifyPicture(int columns, int rows, int firstMacroblock, bool bidirectional)
        {
            int blockColumns = columns * 4;
            int pictureBlocks = blockColumns * rows * 4;
            int lists = bidirectional ? 2 : 1;
            short[][] shadowX = new short[lists][];
            short[][] shadowY = new short[lists][];
            byte[][] shadowConditions = new byte[lists][];
            for (int list = 0; list < lists; list++)
            {
                shadowX[list] = new short[pictureBlocks];
                shadowY[list] = new short[pictureBlocks];
                shadowConditions[list] = new byte[pictureBlocks];
            }
            int budget = blockColumns * Math.Min(rows, 2) * 4 * (bidirectional ? 10 : 4);
            MediaBufferAllocator allocator = new(budget);
            using (H264CabacPredictionWindow window = new(columns * 16, rows * 16, bidirectional, allocator))
            {
                if (allocator.AllocatedBytes != budget)
                {
                    throw new InvalidDataException("CABAC history did not use its exact two-row budget.");
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int address = firstMacroblock; address < columns * rows; address++)
                {
                    window.BeginMacroblock(address);
                    int x = address % columns * 4;
                    int y = address / columns * 4;
                    int partition = 1 << (address % 3);
                    for (int blockY = 0; blockY < 4; blockY += partition)
                    {
                        for (int blockX = 0; blockX < 4; blockX += partition)
                        {
                            for (int list = 0; list < lists; list++)
                            {
                                H264ReferenceList referenceList = list switch
                                {
                                    0 => H264ReferenceList.L0,
                                    1 => H264ReferenceList.L1,
                                    _ => throw new InvalidOperationException()
                                };
                                Check(window, referenceList, x + blockX - 1, y + blockY, blockColumns,
                                    shadowX[list], shadowY[list], shadowConditions[list], bidirectional);
                                Check(window, referenceList, x + blockX, y + blockY - 1, blockColumns,
                                    shadowX[list], shadowY[list], shadowConditions[list], bidirectional);
                                bool coded = (address + blockX + blockY + list) % 5 > 1;
                                if (!coded)
                                {
                                    continue;
                                }
                                for (int dy = 0; dy < partition; dy++)
                                {
                                    for (int dx = 0; dx < partition; dx++)
                                    {
                                        int bx = x + blockX + dx;
                                        int by = y + blockY + dy;
                                        int original = by * blockColumns + bx;
                                        int position = window.BlockIndex(bx * 4, by * 4);
                                        short valueX = (short)((original * 521 + list * 71) % 65536 - 32768);
                                        short valueY = (short)((original * 127 + list * 431) % 65536 - 32768);
                                        window.MotionX(referenceList)[position] = valueX;
                                        window.MotionY(referenceList)[position] = valueY;
                                        shadowX[list][original] = valueX;
                                        shadowY[list][original] = valueY;
                                        if (bidirectional && (address + blockX + list) % 2 == 0)
                                        {
                                            window.ReferenceConditions(referenceList)[position] = 1;
                                            shadowConditions[list][original] = 1;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    for (int by = y; by < y + 4; by++)
                    {
                        for (int bx = x; bx < x + 4; bx++)
                        {
                            for (int list = 0; list < lists; list++)
                            {
                                H264ReferenceList referenceList = list switch
                                {
                                    0 => H264ReferenceList.L0,
                                    1 => H264ReferenceList.L1,
                                    _ => throw new InvalidOperationException()
                                };
                                Check(window, referenceList, bx, by, blockColumns,
                                    shadowX[list], shadowY[list], shadowConditions[list], bidirectional);
                            }
                        }
                    }
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (allocated != 0)
                {
                    throw new InvalidOperationException($"CABAC history allocated {allocated} bytes during traversal of {columns}x{rows} macroblocks from {firstMacroblock}; bidirectional={bidirectional}.");
                }
            }
            if (allocator.AllocatedBytes != 0)
            {
                throw new InvalidOperationException("CABAC history retained storage after disposal.");
            }
            MediaBufferAllocator insufficient = new(budget - 1);
            bool rejected = false;
            try
            {
                using H264CabacPredictionWindow invalid = new(columns * 16, rows * 16, bidirectional, insufficient);
            }
            catch (MediaMemoryLimitException)
            {
                rejected = true;
            }
            if (!rejected || insufficient.AllocatedBytes != 0)
            {
                throw new InvalidDataException("CABAC history did not preserve its allocation limit.");
            }
        }

        private static void Check(H264CabacPredictionWindow window, H264ReferenceList list,
            int x, int y, int columns, short[] shadowX, short[] shadowY, byte[] conditions, bool bidirectional)
        {
            if (x < 0 || y < 0)
            {
                return;
            }
            int full = y * columns + x;
            int compact = window.BlockIndex(x * 4, y * 4);
            if (window.MotionX(list)[compact] != shadowX[full] || window.MotionY(list)[compact] != shadowY[full]
                || (bidirectional && window.ReferenceConditions(list)[compact] != conditions[full]))
            {
                throw new InvalidDataException("CABAC history differs from independent full-picture syntax storage.");
            }
        }
    }
}
