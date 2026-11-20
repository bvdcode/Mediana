// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal class H264CabacPredictionReader(H264CabacReader reader, H264Picture target, MediaBufferAllocator allocator)
        : IH264PredictionReader, IDisposable
    {
        private readonly H264CabacPredictionWindow window = new(target.Width, target.Height, target.HasBidirectionalMotion,
            allocator, target.Layout.MacroblockAdaptiveFrameField);

        public void BeginMacroblock(int address) => window.BeginMacroblock(address);

        public int ReadSubType(ref H264BitReader bits, H264SliceKind kind) => kind switch
        {
            H264SliceKind.P => H264CabacMacroblockSyntax.ReadSubType(ref bits, reader),
            H264SliceKind.B => H264CabacMacroblockSyntax.ReadBidirectionalSubType(ref bits, reader),
            H264SliceKind.I or H264SliceKind.Sp or H264SliceKind.Si => throw new InvalidDataException("The slice has no supported inter sub-macroblock syntax."),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public int ReadReference(ref H264BitReader bits, H264ReferenceList list, int x, int y, int width, int height, int maximum)
        {
            int context = ReferenceCondition(list, x, y, x - 1, y) + 2 * ReferenceCondition(list, x, y, x, y - 1);
            int reference = H264CabacMacroblockSyntax.ReadReference(ref bits, reader, context, maximum);
            if (target.HasBidirectionalMotion && reference > 0)
            {
                Span<byte> conditions = window.ReferenceContextIndices(list);
                for (int row = y; row < y + height; row += 4)
                {
                    for (int column = x; column < x + width; column += 4)
                    {
                        conditions[window.BlockIndex(column, row)] = (byte)reference;
                    }
                }
            }
            return reference;
        }

        public (int X, int Y) ReadMotionDifference(ref H264BitReader bits, H264ReferenceList list, int x, int y, int width, int height)
        {
            Span<short> motionX = window.MotionX(list);
            Span<short> motionY = window.MotionY(list);
            int dx = H264CabacMacroblockSyntax.ReadMotionDifference(ref bits, reader, false,
                DifferenceAt(x, y, x - 1, y, false, motionX) + DifferenceAt(x, y, x, y - 1, false, motionX));
            int dy = H264CabacMacroblockSyntax.ReadMotionDifference(ref bits, reader, true,
                DifferenceAt(x, y, x - 1, y, true, motionY) + DifferenceAt(x, y, x, y - 1, true, motionY));
            for (int row = y; row < y + height; row += 4)
            {
                for (int column = x; column < x + width; column += 4)
                {
                    int index = window.BlockIndex(column, row);
                    motionX[index] = (short)dx;
                    motionY[index] = (short)dy;
                }
            }
            return (dx, dy);
        }

        private int DifferenceAt(int currentX, int currentY, int x, int y, bool vertical, Span<short> differences)
        {
            if (target.Layout.MacroblockAdaptiveFrameField)
            {
                return AdaptiveDifferenceAt(currentX, currentY, x, y, vertical, differences);
            }
            return !target.IsInCurrentSlice(x, y) ? 0 : Math.Abs((int)differences[window.BlockIndex(x, y)]);
        }

        private int ReferenceCondition(H264ReferenceList list, int currentX, int currentY, int x, int y)
        {
            if (target.Layout.MacroblockAdaptiveFrameField)
            {
                return AdaptiveReferenceCondition(list, currentX, currentY, x, y);
            }
            if (!target.IsInCurrentSlice(x, y))
            {
                return 0;
            }
            if (target.HasBidirectionalMotion)
            {
                return window.ReferenceContextIndices(list)[window.BlockIndex(x, y)] > 0 ? 1 : 0;
            }
            byte index = target.ReferenceIndices(list)[target.BlockIndex(0, x, y)];
            return index != byte.MaxValue && index > 0 ? 1 : 0;
        }

        private int AdaptiveDifferenceAt(int currentX, int currentY, int x, int y, bool vertical, Span<short> differences)
        {
            if (AdaptiveNeighbour(currentX, currentY, x, y) is not H264NeighbourLocation neighbour)
            {
                return 0;
            }
            (int nx, int ny) = target.Grid.Position(neighbour.Address);
            int difference = Math.Abs((int)differences[window.BlockIndex(nx + neighbour.X, ny + neighbour.Y)]);
            if (!vertical)
            {
                return difference;
            }
            bool field = target.Grid.IsField(target.Grid.Address(currentX, currentY));
            bool neighbourField = target.Grid.IsField(neighbour.Address);
            return (field, neighbourField) switch
            {
                (true, false) => difference / 2,
                (false, true) => difference * 2,
                (true, true) or (false, false) => difference
            };
        }

        private int AdaptiveReferenceCondition(H264ReferenceList list, int currentX, int currentY, int x, int y)
        {
            if (AdaptiveNeighbour(currentX, currentY, x, y) is not H264NeighbourLocation neighbour)
            {
                return 0;
            }
            (int nx, int ny) = target.Grid.Position(neighbour.Address);
            nx += neighbour.X;
            ny += neighbour.Y;
            bool field = target.Grid.IsField(target.Grid.Address(currentX, currentY));
            int threshold = !field && target.Grid.IsField(neighbour.Address) ? 1 : 0;
            if (target.HasBidirectionalMotion)
            {
                return window.ReferenceContextIndices(list)[window.BlockIndex(nx, ny)] > threshold ? 1 : 0;
            }
            byte index = target.ReferenceIndices(list)[target.BlockIndex(0, nx, ny)];
            return index != byte.MaxValue && index > threshold ? 1 : 0;
        }

        private H264NeighbourLocation? AdaptiveNeighbour(int currentX, int currentY, int x, int y)
        {
            int current = target.Grid.Address(currentX, currentY);
            (int originX, int originY) = target.Grid.Position(current);
            return target.Grid.Neighbour(current, x - originX, y - originY, 16, 16, target.FirstMacroblock);
        }

        public void Dispose() => window.Dispose();
    }
}
