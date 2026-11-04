// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Codecs.H264
{
    internal class H264CabacPredictionReader(H264CabacReader reader, H264Picture target, MediaBufferAllocator allocator)
        : IH264PredictionReader, IDisposable
    {
        private readonly H264CabacPredictionWindow window = new(target.Width, target.Height, target.HasBidirectionalMotion, allocator);

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
            int context = ReferenceCondition(list, x - 1, y) + 2 * ReferenceCondition(list, x, y - 1);
            int reference = H264CabacMacroblockSyntax.ReadReference(ref bits, reader, context, maximum);
            if (target.HasBidirectionalMotion && reference > 0)
            {
                Span<byte> conditions = window.ReferenceConditions(list);
                for (int row = y; row < y + height; row += 4)
                {
                    for (int column = x; column < x + width; column += 4)
                    {
                        conditions[window.BlockIndex(column, row)] = 1;
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
                DifferenceAt(x - 1, y, motionX) + DifferenceAt(x, y - 1, motionX));
            int dy = H264CabacMacroblockSyntax.ReadMotionDifference(ref bits, reader, true,
                DifferenceAt(x - 1, y, motionY) + DifferenceAt(x, y - 1, motionY));
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

        private int DifferenceAt(int x, int y, Span<short> differences) =>
            !target.IsInCurrentSlice(x, y) ? 0 : Math.Abs((int)differences[window.BlockIndex(x, y)]);

        private int ReferenceCondition(H264ReferenceList list, int x, int y)
        {
            if (!target.IsInCurrentSlice(x, y))
            {
                return 0;
            }
            if (target.HasBidirectionalMotion)
            {
                return window.ReferenceConditions(list)[window.BlockIndex(x, y)];
            }
            byte index = target.ReferenceIndices(list)[target.BlockIndex(0, x, y)];
            return index != byte.MaxValue && index > 0 ? 1 : 0;
        }

        public void Dispose() => window.Dispose();
    }
}
