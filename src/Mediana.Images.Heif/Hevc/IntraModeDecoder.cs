// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System;
using System.Runtime.CompilerServices;

namespace Mediana.Images.Heif.Hevc
{
    internal class IntraModeDecoder(IntraCabacReader reader, CodingBlockMap blocks, int log2Ctb)
    {
        private static readonly byte[] HorizontalSubsamplingModes =
        {
            0, 1, 2, 2, 2, 2, 3, 5, 7, 8, 10, 12, 13, 15, 17, 18, 19, 20,
            21, 22, 23, 23, 24, 24, 25, 25, 26, 27, 27, 28, 28, 29, 29, 30, 31
        };

        public void ReadLuma(int x, int y, int size, bool split)
        {
            int count = split ? 4 : 1;
            int blockSize = split ? size / 2 : size;
            Span<bool> previous = stackalloc bool[4];
            for (int i = 0; i < count; i++)
            {
                previous[i] = reader.Read(CabacElement.PreviousIntraLuma);
            }
            Span<int> candidates = stackalloc int[3];
            for (int i = 0; i < count; i++)
            {
                int px = x + ((i & 1) * blockSize);
                int py = y + ((i >> 1) * blockSize);
                int left = blocks.Mode(px - 1, py);
                if (blocks.Partitions is not null && !blocks.Partitions.SameSlice(px - 1, py, px, py))
                {
                    left = 1;
                }
                int top = (py & ((1 << log2Ctb) - 1)) == 0 ? 1 : blocks.Mode(px, py - 1);
                Candidates(left, top, candidates);
                int mode;
                if (previous[i])
                {
                    mode = candidates[reader.Unary(2)];
                }
                else
                {
                    mode = ReadRemainingMode(candidates);
                }

                blocks.SetMode(px, py, blockSize, mode);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadRemainingMode(Span<int> candidates)
        {
            int mode = reader.Bits(5);
            if (candidates[0] > candidates[1])
            {
                (candidates[0], candidates[1]) = (candidates[1], candidates[0]);
            }
            if (candidates[0] > candidates[2])
            {
                (candidates[0], candidates[2]) = (candidates[2], candidates[0]);
            }
            if (candidates[1] > candidates[2])
            {
                (candidates[1], candidates[2]) = (candidates[2], candidates[1]);
            }
            foreach (int candidate in candidates)
            {
                if (mode >= candidate)
                {
                    mode++;
                }
            }
            return mode;
        }

        public int ReadChroma(int lumaMode, int chromaFormat)
        {
            if (!reader.Read(CabacElement.ChromaMode))
            {
                return ChromaMode(lumaMode, chromaFormat);
            }
            int mode;
            switch (reader.Bits(2))
            {
                case 0:
                    mode = 0;
                    break;
                case 1:
                    mode = 26;
                    break;
                case 2:
                    mode = 10;
                    break;
                case 3:
                    mode = 1;
                    break;
                default:
                    throw new InvalidOperationException("Invalid chroma mode index.");
            }

            return ChromaMode(mode == lumaMode ? 34 : mode, chromaFormat);
        }

        private static int ChromaMode(int mode, int chromaFormat)
        {
            switch (chromaFormat)
            {
                case 1:
                case 3:
                    return mode;
                case 2:
                    return HorizontalSubsamplingModes[mode];
                default:
                    throw new ArgumentOutOfRangeException(nameof(chromaFormat));
            }
        }

        private static void Candidates(int left, int top, Span<int> candidates)
        {
            if (left == top)
            {
                if (left < 2)
                {
                    candidates[0] = 0;
                    candidates[1] = 1;
                    candidates[2] = 26;
                }
                else
                {
                    candidates[0] = left;
                    candidates[1] = 2 + ((left + 29) % 32);
                    candidates[2] = 2 + ((left - 1) % 32);
                }
            }
            else
            {
                candidates[0] = left;
                candidates[1] = top;
                candidates[2] = (left != 0) && (top != 0) ? 0 : (left != 1) && (top != 1) ? 1 : 26;
            }
        }
    }
}
