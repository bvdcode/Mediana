// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal class H264CabacMacroblockState : IDisposable
    {
        public const byte Inter = 26;
        public const byte Skipped = 27;
        public const byte Direct = 28;
        private readonly WritableMediaBuffer storage;
        private Memory<byte> syntax;
        private readonly int count;
        private readonly int columns;
        private readonly int firstMacroblock;
        private readonly H264Picture picture;

        public H264CabacMacroblockState(H264Picture picture, MediaBufferAllocator allocator, int firstMacroblock = 0)
        {
            this.picture = picture;
            this.firstMacroblock = firstMacroblock;
            columns = picture.Sequence.WidthInMbs;
            count = picture.Layout.MacroblockCount;
            storage = allocator.Allocate(checked(count * 4));
            syntax = storage.Memory;
        }

        private Span<byte> Syntax
        {
            get
            {
                ObjectDisposedException.ThrowIf(syntax.IsEmpty, this);
                return syntax.Span;
            }
        }

        public Span<byte> Types => Syntax[..count];
        public Span<byte> Patterns => Syntax.Slice(count, count);
        public Span<byte> ChromaModes => Syntax.Slice(count * 2, count);
        public Span<byte> DcFlags => Syntax.Slice(count * 3, count);

        public int Left(int address) => picture.Layout.MacroblockAdaptiveFrameField
            ? picture.Grid.Neighbour(address, -1, 0, 16, 16, firstMacroblock)?.Address ?? -1
            : address % columns == 0 || address - 1 < firstMacroblock ? -1 : address - 1;

        public int Top(int address) => picture.Layout.MacroblockAdaptiveFrameField
            ? picture.Grid.Neighbour(address, 0, -1, 16, 16, firstMacroblock)?.Address ?? -1
            : address - columns < firstMacroblock ? -1 : address - columns;

        public int FieldPairContext(int address)
        {
            int left = address / 2 % columns == 0 ? -1 : address - 2;
            int top = address - columns * 2;
            return 70 + (left >= firstMacroblock && picture.Grid.IsField(left) ? 1 : 0) +
                (top >= firstMacroblock && picture.Grid.IsField(top) ? 1 : 0);
        }

        public int TypeCondition(int neighbour) => neighbour >= 0 && Types[neighbour] != 0 ? 1 : 0;
        public int SkipCondition(int neighbour) => neighbour >= 0 && Types[neighbour] != Skipped ? 1 : 0;

        public int SkipContext(int address, bool? currentPairField = null)
        {
            if (currentPairField is null)
            {
                return SkipCondition(Left(address)) + SkipCondition(Top(address));
            }
            int left = picture.Grid.Neighbour(address, -1, 0, 16, 16, firstMacroblock, currentPairField)?.Address ?? -1;
            int top = picture.Grid.Neighbour(address, 0, -1, 16, 16, firstMacroblock, currentPairField)?.Address ?? -1;
            return SkipCondition(left) + SkipCondition(top);
        }
        public int BidirectionalTypeCondition(int neighbour) => neighbour >= 0 && Types[neighbour] is not (Skipped or Direct) ? 1 : 0;
        public bool IsIntra(int address) => Types[address] <= 25;

        public int ChromaModeCondition(int neighbour) =>
            neighbour >= 0 && Types[neighbour] != 25 && ChromaModes[neighbour] != 0 ? 1 : 0;

        public int ChromaPatternCondition(int neighbour, int bin)
        {
            if (neighbour < 0)
            {
                return 0;
            }
            if (Types[neighbour] == 25)
            {
                return 1;
            }
            int chroma = Patterns[neighbour] >> 4;
            return bin == 0 ? (chroma != 0 ? 1 : 0) : (chroma == 2 ? 1 : 0);
        }

        public int LumaPatternCondition(int address, int block, bool left, int currentPattern)
        {
            if (picture.Layout.MacroblockAdaptiveFrameField)
            {
                return MappedLumaPatternCondition(address, block, left, currentPattern);
            }
            int column = block % 2;
            int row = block / 2;
            int neighbour = address;
            if (left)
            {
                if (column == 0)
                {
                    neighbour = Left(address);
                    column = 1;
                }
                else
                {
                    column--;
                }
            }
            else if (row == 0)
            {
                neighbour = Top(address);
                row = 1;
            }
            else
            {
                row--;
            }
            if (neighbour < 0 || Types[neighbour] == 25)
            {
                return 0;
            }
            int pattern = neighbour == address ? currentPattern : Patterns[neighbour];
            return (pattern & (1 << (row * 2 + column))) == 0 ? 1 : 0;
        }

        private int MappedLumaPatternCondition(int address, int block, bool left, int currentPattern)
        {
            int x = block % 2 * 8 - (left ? 1 : 0);
            int y = block / 2 * 8 - (left ? 0 : 1);
            if (picture.Grid.Neighbour(address, x, y, 16, 16, firstMacroblock) is not H264NeighbourLocation neighbour ||
                Types[neighbour.Address] == 25)
            {
                return 0;
            }
            int pattern = neighbour.Address == address ? currentPattern : Patterns[neighbour.Address];
            int index = neighbour.Y / 8 * 2 + neighbour.X / 8;
            return (pattern & (1 << index)) == 0 ? 1 : 0;
        }

        public int DcCondition(int neighbour, int plane, bool currentIsIntra)
        {
            if (neighbour < 0)
            {
                return currentIsIntra ? 1 : 0;
            }
            return Types[neighbour] == 25 || (DcFlags[neighbour] & (1 << plane)) != 0 ? 1 : 0;
        }

        public void Dispose()
        {
            syntax = default;
            storage.Dispose();
        }
    }
}
